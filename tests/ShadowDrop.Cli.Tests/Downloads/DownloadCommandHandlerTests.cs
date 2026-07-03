// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Shares;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Terminals;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using ShadowDrop.Queue;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[NonParallelizable]
public sealed class DownloadCommandHandlerTests
{
    private const String ValidShareKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void DecodeShareKey_ShouldReturn32Bytes_WhenValueIsValidHex()
    {
        DownloadCommandHandler.DecodeShareKey(ValidShareKey).Should().HaveCount(32);
    }

    [Test]
    public void DecodeShareKey_ShouldStripSecretPrefix_WhenPresent()
    {
        DownloadCommandHandler.DecodeShareKey($"  secret:{ValidShareKey}  ").Should().HaveCount(32);
    }

    [Test]
    public void DecodeShareKey_ShouldThrow_WhenLengthIsNot32Bytes()
    {
        var act = () => DownloadCommandHandler.DecodeShareKey("aabbcc");

        act.Should().Throw<DownloadCommandException>().WithMessage("Share key invalid or missing.");
    }

    [Test]
    public void DecodeShareKey_ShouldThrow_WhenValueIsNotHex()
    {
        var act = () => DownloadCommandHandler.DecodeShareKey("zz");

        act.Should().Throw<DownloadCommandException>().WithMessage("Share key invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldContinueQueueProcessingAfterIndividualFailures()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("stable.bin", 72);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = upload.Share;
        using var httpClient = fixture.CreateClient();
        var outputsDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputsDirectory);
        var queuePath = fixture.CreateQueueFile(new()
        {
            ShareToken = share.ShareToken,
            ServerUrl = httpClient.BaseAddress!.ToString(),
            Entries =
            [
                new(upload.FileIds[0].ToString(), "stable.bin", 72, "stable.bin"),
                new(Guid.NewGuid().ToString(), "missing.bin", 88, "missing.bin")
            ]
        });
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", outputsDirectory, "--share-key", upload.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath,
                                                                       httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "stable.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(inputFile));
        File.Exists(Path.Combine(outputsDirectory, "missing.bin")).Should().BeFalse();
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("SUCCESS 1/2 stable.bin -> stable.bin")
                     .And.Contain("FAILED 2/2 missing.bin -> missing.bin")
                     .And.Contain("Requested file not found in share.")
                     .And.Contain("SUMMARY downloaded 1/2 files");
    }

    [Test]
    public async Task InvokeAsync_ShouldContinueQueueProcessingAfterManifestFetchFailure()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var firstOutputPath = Path.Combine(rootDirectory, "broken.bin");
            var secondOutputPath = Path.Combine(rootDirectory, "good.bin");
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "broken-share",
                        FileId = fixture.FileId.ToString(),
                        FileName = "broken.bin",
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "broken.bin"
                    },
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "good-share",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "good.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);

            using var handler = new SequenceHttpMessageHandler(
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/broken-share"));
                    throw new HttpRequestException("Simulated manifest fetch failure.");
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/good-share"));
                    return fixture.CreateManifestResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/good-share/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            binaryOutput.Length.Should().Be(0);
            standardOut.ToString().Should().BeEmpty();
            File.Exists(firstOutputPath).Should().BeFalse();
            (await File.ReadAllBytesAsync(secondOutputPath)).Should().Equal(fixture.Plaintext);
            standardError.ToString().Should().Contain("FAILED 1/2 broken.bin -> broken.bin: Server connection failed.")
                         .And.Contain($"SUCCESS 2/2 {fixture.FileName} -> good.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldContinueQueueProcessingWhenManifestContainsDuplicateFileIds()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var firstOutputPath = Path.Combine(rootDirectory, "broken.bin");
            var secondOutputPath = Path.Combine(rootDirectory, "good.bin");
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "broken-share",
                        FileId = fixture.FileId.ToString(),
                        FileName = "broken.bin",
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "broken.bin"
                    },
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "good-share",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "good.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);

            using var handler = new SequenceHttpMessageHandler(
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/broken-share"));
                    return fixture.CreateManifestResponseWithFiles(
                        fixture.CreateManifestFile("broken.bin"),
                        fixture.CreateManifestFile("duplicate.bin"));
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/good-share"));
                    return fixture.CreateManifestResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/good-share/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            binaryOutput.Length.Should().Be(0);
            standardOut.ToString().Should().BeEmpty();
            File.Exists(firstOutputPath).Should().BeFalse();
            (await File.ReadAllBytesAsync(secondOutputPath)).Should().Equal(fixture.Plaintext);
            standardError.ToString().Should().Contain("FAILED 1/2 broken.bin -> broken.bin: Share metadata invalid or missing.")
                         .And.Contain($"SUCCESS 2/2 {fixture.FileName} -> good.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldCreateResumeMarkerOwnerOnly_WhenFileDownloadIsInterrupted()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required to validate owner-only marker mode.");
            return;
        }

        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var markerPath = $"{outputPath}.shadowdrop-partial.json";
            var queuePath = CreateQueueFile(rootDirectory, fixture);
            var firstChunkEncryptedLength = fixture.EncryptedChunks[0].Length;

            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                _ => fixture.CreateInterruptedDownloadResponse(firstChunkEncryptedLength + 7));
            using var httpClient = new HttpClient(handler);

            var exitCode = await CliApplication.InvokeAsync(
                ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                CancellationToken.None);

            exitCode.Should().Be(1);
            File.Exists(markerPath).Should().BeTrue();
            File.GetUnixFileMode(markerPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldDeletePartialAndMarker_WhenFileVerificationFails()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var wrongSha256 = new String('0', 64);
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            var queuePath = CreateQueueFile(rootDirectory, fixture, wrongSha256);
            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(plaintextSha256: wrongSha256),
                _ => fixture.CreateDownloadResponse());
            using var httpClient = new HttpClient(handler);
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(
                ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                CreateServices(new MemoryStream(), new StringWriter(), standardError, httpClient: httpClient),
                CancellationToken.None);

            exitCode.Should().Be(1);
            File.Exists(outputPath).Should().BeFalse();
            File.Exists(partialPath).Should().BeFalse();
            File.Exists(markerPath).Should().BeFalse();
            standardError.ToString().Should().Contain("Downloaded file did not match the shared file.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldDeleteStaleQueuePartialAndRestartFromByteZero()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            await File.WriteAllBytesAsync(partialPath, [1, 2, 3, 4]);
            await WriteResumeMarkerAsync(markerPath, "https://shadowdrop.test/base-path/", "shared-token", Guid.NewGuid().ToString("D"),
                                         fixture.FileName, fixture.Plaintext.LongLength, Convert.ToBase64String(fixture.KdfSalt), null);
            var queuePath = CreateQueueFile(rootDirectory, fixture);

            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                request =>
                {
                    request.Headers.Range.Should().BeNull();
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            File.Exists(partialPath).Should().BeFalse();
            File.Exists(markerPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldDownloadSingleFileToStdout_WhenCliShareKeyOverridesKeyFile()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("report.bin", 128);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = upload.Share;
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var invalidKeyFile = Path.Combine(fixture.RootDirectory, "invalid-key.txt");
        await File.WriteAllTextAsync(invalidKeyFile, "not-a-secret");
        using var httpClient = fixture.CreateClient();

        var exitCode = await CliApplication.InvokeAsync(
            [
                "download", share.ShareToken, "--server-url", httpClient.BaseAddress!.ToString(), "--share-key", upload.ShareKey,
                "--share-key-file", invalidKeyFile
            ],
            CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient),
            CancellationToken.None);

        exitCode.Should().Be(0);
        binaryOutput.ToArray().Should().Equal(await File.ReadAllBytesAsync(inputFile));
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("START report.bin")
                     .And.Contain("SUCCESS report.bin")
                     .And.Contain("SUMMARY downloaded 1 file");
    }

    [Test]
    public async Task InvokeAsync_ShouldExplainSecretFreeQueue_WhenShareKeyMissing()
    {
        await using var fixture = new CliDownloadApiFactory();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var queuePath = Path.Combine(fixture.RootDirectory, "secret-free.queue.json");
        await File.WriteAllTextAsync(queuePath,
                                     """
                                     {
                                       "shadowDrop": "1.0",
                                       "queueVersion": "1.0",
                                       "files": [
                                         {
                                           "serverUrl": "https://shadowdrop.test",
                                           "shareToken": "share-123",
                                           "fileId": "file-1",
                                           "fileName": "report.txt",
                                           "length": 4096,
                                           "outputPath": "report.txt"
                                         }
                                       ]
                                     }
                                     """);

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath],
                                                        CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("secret-free")
                     .And.Contain("--share-key")
                     .And.Contain("--embed-secrets");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenConfigurationFileIsMalformed()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{ not valid json");
        var standardError = new StringWriter();
        try
        {
            var exitCode = await CliApplication.InvokeAsync(["download", "plain-share-token", "--share-key", ValidShareKey],
                                                            CreateServices(Stream.Null, new StringWriter(), standardError, configPath),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            standardError.ToString().Should().Contain("Configuration file invalid or unreadable.");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenNeitherShareTokenNorQueueProvided()
    {
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--share-key", ValidShareKey],
                                                        CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Specify either a share token or --queue.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenQueueCombinedWithShareToken()
    {
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(
            ["download", "some-share", "--queue", "queue.json", "--share-key", ValidShareKey],
            CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("The --queue option cannot be combined with a share token, --file, or --server-url.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenQueueFileIsMalformedJson()
    {
        var queuePath = Path.Combine(Path.GetTempPath(), $"queue-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(queuePath, "{ not valid json");
        var standardError = new StringWriter();
        try
        {
            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", ValidShareKey],
                                                            CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

            exitCode.Should().Be(1);
            standardError.ToString().Should().Contain("The queue file is invalid.");
        }
        finally
        {
            File.Delete(queuePath);
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenShareKeyFileCannotBeRead()
    {
        var standardError = new StringWriter();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.key");

        var exitCode = await CliApplication.InvokeAsync(["download", "some-share", "--share-key-file", missingPath],
                                                        CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Share key invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenShareKeyIsInvalid()
    {
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "some-share", "--share-key", "not-hex"],
                                                        CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Share key invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenShareUrlHasInvalidPath()
    {
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(
            ["download", "https://shadowdrop.test/not-a-share", "--share-key", ValidShareKey],
            CreateServices(Stream.Null, new StringWriter(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Share token invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFailQueueEntry_WhenQueueAndManifestPlaintextHashesConflict()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var queuePath = CreateQueueFile(rootDirectory, fixture, ComputeSha256(fixture.Plaintext));
            using var handler = new SequenceHttpMessageHandler(_ => fixture.CreateManifestResponse(plaintextSha256: new('0', 64)));
            using var httpClient = new HttpClient(handler);
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            File.Exists(outputPath).Should().BeFalse();
            standardError.ToString().Should().Contain("Queue entry does not match share metadata.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldFailQueueEntryAndLeaveCompletedOutputUntouched_WhenExistingOutputHashDoesNotMatch()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var existingBytes = fixture.Plaintext.ToArray();
            existingBytes[0] ^= 0xff;
            await File.WriteAllBytesAsync(outputPath, existingBytes);
            var queuePath = CreateQueueFile(rootDirectory, fixture, ComputeSha256(fixture.Plaintext));
            using var handler = new SequenceHttpMessageHandler(_ => fixture.CreateManifestResponse());
            using var httpClient = new HttpClient(handler);
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(existingBytes);
            standardError.ToString().Should().Contain("Existing output file does not match");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldFailWhenShareKeyMissingWithoutPrompting()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "share-123"], CreateServices(Stream.Null, standardOut, standardError),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Share key invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldGuideInteractiveDownloadWithMaskedSecretsAndNoLeakage()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("interactive-download.bin", 72);
        var upload = await fixture.UploadFilesAsync([inputFile], requireDownloadToken: true);
        var share = upload.Share;
        using var httpClient = fixture.CreateClient();
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession();
        var outputPath = Path.Combine(fixture.RootDirectory, "interactive-download.bin");
        interactiveSession.EnqueueTextResponse(upload.ShareKey);
        interactiveSession.EnqueueTextResponse(share.DownloadBearerToken!);
        interactiveSession.EnqueueMultiSelection(0);
        interactiveSession.EnqueueTextResponse(outputPath);

        var exitCode = await CliApplication.InvokeAsync(["download", "--interactive", "--server-url", httpClient.BaseAddress!.ToString(), share.ShareToken],
                                                        CreateServices(binaryOutput,
                                                                       standardOut,
                                                                       standardError,
                                                                       fixture.ConfigFilePath,
                                                                       httpClient: httpClient,
                                                                       interactiveSession: interactiveSession),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        (await File.ReadAllBytesAsync(outputPath)).Should().BeEquivalentTo(await File.ReadAllBytesAsync(inputFile));
        standardError.ToString().Should().NotContain(upload.ShareKey)
                     .And.NotContain(share.DownloadBearerToken!);
        interactiveSession.Summaries.Should().Contain(summary => summary.Title == "Download complete");
        interactiveSession.TextPrompts.Should().Contain(("Share key:", true))
                          .And.Contain(("Download bearer token:", true));
    }

    [Test]
    public async Task InvokeAsync_ShouldHonorBearerTokenArgument_ForProtectedShares()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("protected.bin", 96);
        var upload = await fixture.UploadFilesAsync([inputFile], requireDownloadToken: true);
        var share = upload.Share;
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();

        var exitCode = await CliApplication.InvokeAsync(
            [
                "download", share.ShareToken, "--server-url", httpClient.BaseAddress!.ToString(), "--share-key", upload.ShareKey, "--bearer-token",
                share.DownloadBearerToken!
            ],
            CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient),
            CancellationToken.None);

        exitCode.Should().Be(0);
        binaryOutput.ToArray().Should().Equal(await File.ReadAllBytesAsync(inputFile));
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("START protected.bin")
                     .And.Contain("SUCCESS protected.bin")
                     .And.Contain("SUMMARY downloaded 1 file");
    }

    [Test]
    public async Task InvokeAsync_ShouldIgnoreInvalidConfigFileWhenQueueEntriesProvideServerUrls()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var configPath = Path.Combine(rootDirectory, "config.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "payload.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);
            await File.WriteAllTextAsync(configPath, "{not-json");

            using var handler = new SequenceHttpMessageHandler(
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/shared-token"));
                    return fixture.CreateManifestResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/shared-token/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, configPath, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            binaryOutput.Length.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain($"SUCCESS 1/1 {fixture.FileName} -> payload.bin")
                         .And.NotContain("Configuration file invalid or unreadable.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldMatchQueueFileIdsIgnoringGuidCasing()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("stable.bin", 72);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = upload.Share;
        using var httpClient = fixture.CreateClient();
        var outputsDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputsDirectory);
        var outputPath = Path.Combine(outputsDirectory, "stable.bin");
        var queuePath = fixture.CreateQueueFile(new()
        {
            ShareToken = share.ShareToken,
            ServerUrl = httpClient.BaseAddress!.ToString(),
            Entries =
            [
                new(upload.FileIds[0].ToString().ToUpperInvariant(), "stable.bin", 72, "stable.bin")
            ]
        });
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", outputsDirectory, "--share-key", upload.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath,
                                                                       httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(outputPath)).Should().BeEquivalentTo(await File.ReadAllBytesAsync(inputFile));
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("SUCCESS 1/1 stable.bin -> stable.bin");
    }

    [Test]
    public async Task InvokeAsync_ShouldMatchRequestedFileIdIgnoringGuidCasing()
    {
        await using var fixture = new CliDownloadApiFactory();
        var firstInput = fixture.CreateInputFile("alpha.bin", 64);
        var secondInput = fixture.CreateInputFile("beta.bin", 80);
        var upload = await fixture.UploadFilesAsync([firstInput, secondInput]);
        var share = upload.Share;
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();

        var exitCode = await CliApplication.InvokeAsync(
            [
                "download", share.ShareToken, "--server-url", httpClient.BaseAddress!.ToString(), "--share-key", upload.ShareKey, "--file",
                upload.FileIds[1].ToString().ToUpperInvariant()
            ],
            CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient),
            CancellationToken.None);

        exitCode.Should().Be(0);
        binaryOutput.ToArray().Should().Equal(await File.ReadAllBytesAsync(secondInput));
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("START beta.bin")
                     .And.Contain("SUCCESS beta.bin")
                     .And.Contain("SUMMARY downloaded 1 file");
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveAndResumeQueuePartialAfterCancellation()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            var queuePath = CreateQueueFile(rootDirectory, fixture);
            var firstChunkEncryptedLength = fixture.EncryptedChunks[0].Length;
            using var cancellation = new CancellationTokenSource();

            using (var handler = new SequenceHttpMessageHandler(
                       _ => fixture.CreateManifestResponse(),
                       _ => fixture.CreateCancellingDownloadResponse(firstChunkEncryptedLength + 7, cancellation)))
            using (var httpClient = new HttpClient(handler))
            {
                var firstAttempt = async () => await CliApplication.InvokeAsync(
                    ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                    CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                    cancellation.Token);

                await firstAttempt.Should().ThrowAsync<OperationCanceledException>();
            }

            File.Exists(outputPath).Should().BeFalse();
            File.Exists(partialPath).Should().BeTrue();
            File.Exists(markerPath).Should().BeTrue();
            (await File.ReadAllBytesAsync(partialPath)).Should().Equal(fixture.Plaintext.Take(fixture.ChunkSize));

            using (var handler = new SequenceHttpMessageHandler(
                       _ => fixture.CreateManifestResponse(),
                       request =>
                       {
                           var range = request.Headers.Range;
                           range.Should().NotBeNull();
                           range!.Ranges.Should().ContainSingle();
                           range.Ranges.Single().From.Should().Be(fixture.ChunkSize);
                           range.Ranges.Single().To.Should().Be(fixture.Plaintext.LongLength - 1);
                           return fixture.CreateDownloadResponse(new()
                           {
                               Start = fixture.ChunkSize,
                               End = fixture.Plaintext.LongLength
                           });
                       }))
            using (var httpClient = new HttpClient(handler))
            {
                var secondExitCode = await CliApplication.InvokeAsync(
                    ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                    CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                    CancellationToken.None);

                secondExitCode.Should().Be(0);
            }

            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            File.Exists(partialPath).Should().BeFalse();
            File.Exists(markerPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveAndResumeQueuePartialAfterInterruptedFileDownload()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            var queuePath = CreateQueueFile(rootDirectory, fixture);
            var firstChunkEncryptedLength = fixture.EncryptedChunks[0].Length;

            using (var handler = new SequenceHttpMessageHandler(
                       _ => fixture.CreateManifestResponse(),
                       _ => fixture.CreateInterruptedDownloadResponse(firstChunkEncryptedLength + 7)))
            using (var httpClient = new HttpClient(handler))
            {
                var firstExitCode = await CliApplication.InvokeAsync(
                    ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                    CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                    CancellationToken.None);

                firstExitCode.Should().Be(1);
            }

            File.Exists(outputPath).Should().BeFalse();
            File.Exists(partialPath).Should().BeTrue();
            File.Exists(markerPath).Should().BeTrue();
            (await File.ReadAllBytesAsync(partialPath)).Should().Equal(fixture.Plaintext.Take(fixture.ChunkSize));

            var secondStandardError = new StringWriter();
            using (var handler = new SequenceHttpMessageHandler(
                       _ => fixture.CreateManifestResponse(),
                       request =>
                       {
                           var range = request.Headers.Range;
                           range.Should().NotBeNull();
                           range!.Ranges.Should().ContainSingle();
                           range.Ranges.Single().From.Should().Be(fixture.ChunkSize);
                           range.Ranges.Single().To.Should().Be(fixture.Plaintext.LongLength - 1);
                           return fixture.CreateDownloadResponse(new()
                           {
                               Start = fixture.ChunkSize,
                               End = fixture.Plaintext.LongLength
                           });
                       }))
            using (var httpClient = new HttpClient(handler))
            {
                var secondExitCode = await CliApplication.InvokeAsync(
                    ["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                    CreateServices(new MemoryStream(), new StringWriter(), secondStandardError, httpClient: httpClient),
                    CancellationToken.None);

                secondExitCode.Should().Be(0);
            }

            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            // Only the 64 B transferred during this resumed run is reported, not the file's full 128 B size.
            secondStandardError.ToString().Should().Contain("SUCCESS 1/1 payload.bin -> payload.bin (64 B in");
            File.Exists(partialPath).Should().BeFalse();
            File.Exists(markerPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveBasePathForManifestAndFileRequests()
    {
        var fixture = DownloadHttpFixture.Create();
        using var handler = new SequenceHttpMessageHandler(
            request =>
            {
                request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/share-token"));
                return fixture.CreateManifestResponse();
            },
            request =>
            {
                request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/share-token/files/{fixture.FileId:D}?mode=cli"));
                return fixture.CreateDownloadResponse();
            });
        using var httpClient = new HttpClient(handler);
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "https://shadowdrop.test/base-path/d/share-token", "--share-key", fixture.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        binaryOutput.ToArray().Should().Equal(fixture.Plaintext);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain($"START {fixture.FileName}")
                     .And.Contain($"SUCCESS {fixture.FileName}")
                     .And.Contain("SUMMARY downloaded 1 file");
    }

    [Test]
    public async Task InvokeAsync_ShouldProcessQueueAndWritePerFileSummaryToStderr()
    {
        await using var fixture = new CliDownloadApiFactory();
        var firstInput = fixture.CreateInputFile("alpha.bin", 64);
        var secondInput = fixture.CreateInputFile("beta.bin", 80);
        var upload = await fixture.UploadFilesAsync([firstInput, secondInput]);
        var share = upload.Share;
        using var httpClient = fixture.CreateClient();
        var outputsDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputsDirectory);
        var queuePath = fixture.CreateQueueFile(new()
        {
            ShareToken = share.ShareToken,
            ServerUrl = httpClient.BaseAddress!.ToString(),
            Entries =
            [
                new(upload.FileIds[0].ToString(), "alpha.bin", 64, "alpha.bin"),
                new(upload.FileIds[1].ToString(), "beta.bin", 80, "beta.bin")
            ]
        });
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", outputsDirectory, "--share-key", upload.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath,
                                                                       httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "alpha.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(firstInput));
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "beta.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(secondInput));
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("SUCCESS 1/2 alpha.bin -> alpha.bin")
                     .And.Contain("SUCCESS 2/2 beta.bin -> beta.bin")
                     .And.Contain("SUMMARY downloaded 2/2 files");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectInvalidQueueBeforeDownloading()
    {
        await using var fixture = new CliDownloadApiFactory();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var queuePath = Path.Combine(fixture.RootDirectory, "invalid.queue.json");
        await File.WriteAllTextAsync(queuePath,
                                     """
                                     {
                                       "shadowDrop": "1.0",
                                       "queueVersion": "1.0",
                                       "files": [
                                         {
                                           "serverUrl": "https://shadowdrop.test",
                                           "shareToken": "share-123",
                                           "fileId": "file-1",
                                           "fileName": "report.txt",
                                           "length": 4096
                                         }
                                       ]
                                     }
                                     """);

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", new('a', 64)],
                                                        CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("files[0].outputPath: The outputPath value is required.")
                     .And.Contain("The queue file is invalid.");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectOutputRootWithoutQueue()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession();

        var exitCode = await CliApplication.InvokeAsync(["download", "--interactive", "--output-root", "downloads"],
                                                        CreateServices(Stream.Null,
                                                                       standardOut,
                                                                       standardError,
                                                                       httpClient: new HttpClient(new NeverCalledHandler()),
                                                                       interactiveSession: interactiveSession),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("The --output-root option requires --queue.");
        interactiveSession.TextPrompts.Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectQueueOutputPathEscapingIntoSiblingDirectorySharingRootPrefix()
    {
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(rootDirectory, "downloads");
        Directory.CreateDirectory(outputRoot);

        try
        {
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = Guid.NewGuid().ToString(),
                        FileName = "report.txt",
                        Length = 4096,
                        OutputPath = "../downloads-evil/report.txt"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", outputRoot, "--share-key", ValidShareKey],
                                                            CreateServices(binaryOutput,
                                                                           standardOut,
                                                                           standardError,
                                                                           httpClient: new HttpClient(new NeverCalledHandler())),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            binaryOutput.Length.Should().Be(0);
            standardOut.ToString().Should().BeEmpty();
            File.Exists(Path.Combine(rootDirectory, "downloads-evil", "report.txt")).Should().BeFalse();
            standardError.ToString().Should().Contain("FAILED 1/1 report.txt -> ../downloads-evil/report.txt: Queue output path escapes the output root.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectQueueOutputPathEscapingOutputRootBeforeDownloading()
    {
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(rootDirectory, "downloads");
        Directory.CreateDirectory(outputRoot);

        try
        {
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = Guid.NewGuid().ToString(),
                        FileName = "report.txt",
                        Length = 4096,
                        OutputPath = "../outside.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", outputRoot, "--share-key", ValidShareKey],
                                                            CreateServices(binaryOutput,
                                                                           standardOut,
                                                                           standardError,
                                                                           httpClient: new HttpClient(new NeverCalledHandler())),
                                                            CancellationToken.None);

            exitCode.Should().Be(1);
            binaryOutput.Length.Should().Be(0);
            standardOut.ToString().Should().BeEmpty();
            File.Exists(Path.Combine(rootDirectory, "outside.bin")).Should().BeFalse();
            standardError.ToString().Should().Contain("FAILED 1/1 report.txt -> ../outside.bin: Queue output path escapes the output root.");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldReportManifestReadFailuresAsCommandErrors()
    {
        using var handler = new SequenceHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingReadStream(new IOException("Simulated manifest read failure.")))
        });
        using var httpClient = new HttpClient(handler);
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "share-token", "--server-url", "https://shadowdrop.test", "--share-key", new('a', 64)],
                                                        CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Server connection failed.")
                     .And.NotContain("local I/O");
    }

    [Test]
    public async Task InvokeAsync_ShouldResetQueuePartialWhenResumeMarkerCannotBeRead()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required to make the marker unreadable without changing the parent directory.");
            return;
        }

        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            await File.WriteAllBytesAsync(partialPath, fixture.Plaintext.Take(fixture.ChunkSize).ToArray());
            await WriteResumeMarkerAsync(markerPath,
                                         "https://shadowdrop.test/base-path/",
                                         "shared-token",
                                         fixture.FileId.ToString("D"),
                                         fixture.FileName,
                                         fixture.Plaintext.LongLength,
                                         Convert.ToBase64String(fixture.KdfSalt),
                                         null);
            File.SetUnixFileMode(markerPath, UnixFileMode.None);
            var queuePath = CreateQueueFile(rootDirectory, fixture);

            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                request =>
                {
                    request.Headers.Range.Should().BeNull();
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
        }
        finally
        {
            if (!OperatingSystem.IsWindows() && File.Exists(Path.Combine(rootDirectory, "payload.bin.shadowdrop-partial.json")))
            {
                File.SetUnixFileMode(Path.Combine(rootDirectory, "payload.bin.shadowdrop-partial.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldResolveQueueOutputPathUnderCurrentDirectory_WhenOutputRootOmitted()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var originalCurrentDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = rootDirectory;
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "default-root.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);

            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                _ => fixture.CreateDownloadResponse());
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(Path.Combine(rootDirectory, "default-root.bin"))).Should().Equal(fixture.Plaintext);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain($"SUCCESS 1/1 {fixture.FileName} -> default-root.bin");
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldResumeFirstIncompleteQueueEntryAndContinueRemainingEntries()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var firstOutputPath = Path.Combine(rootDirectory, "payload.bin");
            var secondOutputPath = Path.Combine(rootDirectory, "second.bin");
            var partialPath = $"{firstOutputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            await File.WriteAllBytesAsync(partialPath, fixture.Plaintext.Take(fixture.ChunkSize).ToArray());
            await WriteResumeMarkerAsync(markerPath,
                                         "https://shadowdrop.test/base-path/",
                                         "shared-token",
                                         fixture.FileId.ToString("D"),
                                         fixture.FileName,
                                         fixture.Plaintext.LongLength,
                                         Convert.ToBase64String(fixture.KdfSalt),
                                         null);
            var queuePath = CreateQueueFile(rootDirectory, fixture, null, "payload.bin", "second.bin");
            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                request =>
                {
                    request.Headers.Range.Should().NotBeNull();
                    request.Headers.Range!.Ranges.Single().From.Should().Be(fixture.ChunkSize);
                    return fixture.CreateDownloadResponse(new()
                    {
                        Start = fixture.ChunkSize,
                        End = fixture.Plaintext.LongLength
                    });
                },
                request =>
                {
                    request.Headers.Range.Should().BeNull();
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(firstOutputPath)).Should().Equal(fixture.Plaintext);
            (await File.ReadAllBytesAsync(secondOutputPath)).Should().Equal(fixture.Plaintext);
            standardError.ToString().Should().Contain($"SUCCESS 1/2 {fixture.FileName} -> payload.bin")
                         .And.Contain($"SUCCESS 2/2 {fixture.FileName} -> second.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldResumeInteractiveFileDownloadFromValidatedPartial()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "interactive.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            await File.WriteAllBytesAsync(partialPath, fixture.Plaintext.Take(fixture.ChunkSize).ToArray());
            await WriteResumeMarkerAsync(markerPath,
                                         "https://shadowdrop.test/base-path/",
                                         "shared-token",
                                         fixture.FileId.ToString("D"),
                                         fixture.FileName,
                                         fixture.Plaintext.LongLength,
                                         Convert.ToBase64String(fixture.KdfSalt),
                                         null);
            var interactiveSession = new FakeInteractiveSession();
            interactiveSession.EnqueueTextResponse(fixture.ShareKey);
            interactiveSession.EnqueueMultiSelection(0);
            interactiveSession.EnqueueTextResponse(outputPath);
            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                request =>
                {
                    request.Headers.Range.Should().NotBeNull();
                    request.Headers.Range!.Ranges.Single().From.Should().Be(fixture.ChunkSize);
                    return fixture.CreateDownloadResponse(new()
                    {
                        Start = fixture.ChunkSize,
                        End = fixture.Plaintext.LongLength
                    });
                });
            using var httpClient = new HttpClient(handler);

            var exitCode = await CliApplication.InvokeAsync(
                ["download", "--interactive", "--server-url", "https://shadowdrop.test/base-path/", "shared-token"],
                CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient, interactiveSession: interactiveSession),
                CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            interactiveSession.Summaries.Should().Contain(summary => summary.Title == "Download complete");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldResumeQueuePartialWithEquivalentServerUrlVariant()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            var partialPath = $"{outputPath}.shadowdrop-partial";
            var markerPath = $"{partialPath}.json";
            await File.WriteAllBytesAsync(partialPath, fixture.Plaintext.Take(fixture.ChunkSize).ToArray());
            await WriteResumeMarkerAsync(markerPath,
                                         "https://shadowdrop.test/base-path/",
                                         "shared-token",
                                         fixture.FileId.ToString("D"),
                                         fixture.FileName,
                                         fixture.Plaintext.LongLength,
                                         Convert.ToBase64String(fixture.KdfSalt),
                                         null);
            var queuePath = CreateQueueFileWithServerUrl(rootDirectory, fixture, "https://shadowdrop.test/base-path");

            using var handler = new SequenceHttpMessageHandler(
                _ => fixture.CreateManifestResponse(),
                request =>
                {
                    request.Headers.Range.Should().NotBeNull();
                    request.Headers.Range!.Ranges.Single().From.Should().Be(fixture.ChunkSize);
                    return fixture.CreateDownloadResponse(new()
                    {
                        Start = fixture.ChunkSize,
                        End = fixture.Plaintext.LongLength
                    });
                });
            using var httpClient = new HttpClient(handler);

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), new StringWriter(), httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldReturnFailureAfterStdoutDownload_WhenPlaintextHashDoesNotMatch()
    {
        var fixture = DownloadHttpFixture.Create();
        using var handler = new SequenceHttpMessageHandler(
            _ => fixture.CreateManifestResponse(plaintextSha256: new('0', 64)),
            _ => fixture.CreateDownloadResponse());
        using var httpClient = new HttpClient(handler);
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(
            ["download", "shared-token", "--server-url", "https://shadowdrop.test/base-path/", "--share-key", fixture.ShareKey],
            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
            CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.ToArray().Should().Equal(fixture.Plaintext);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Downloaded file hash did not match the shared file.");
    }

    [Test]
    public async Task InvokeAsync_ShouldReturnFailureAfterStdoutDownload_WhenPlaintextLengthDoesNotMatch()
    {
        var fixture = DownloadHttpFixture.Create();
        var manifestFile = fixture.CreateManifestFile() with
        {
            Length = fixture.Plaintext.LongLength + 1
        };
        using var handler = new SequenceHttpMessageHandler(
            _ => fixture.CreateManifestResponseWithFiles(manifestFile),
            _ => fixture.CreateDownloadResponse());
        using var httpClient = new HttpClient(handler);
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(
            ["download", "shared-token", "--server-url", "https://shadowdrop.test/base-path/", "--share-key", fixture.ShareKey],
            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
            CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.ToArray().Should().Equal(fixture.Plaintext);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Downloaded file length mismatch");
    }

    [Test]
    public async Task InvokeAsync_ShouldReuseManifestCacheForEquivalentServerUrlVariants()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var firstOutputPath = Path.Combine(rootDirectory, "first.bin");
            var secondOutputPath = Path.Combine(rootDirectory, "second.bin");
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString().ToUpperInvariant(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "first.bin"
                    },
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "second.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);

            var manifestRequests = 0;
            using var handler = new SequenceHttpMessageHandler(
                request =>
                {
                    manifestRequests++;
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/shared-token"));
                    return fixture.CreateManifestResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/shared-token/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/shared-token/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            manifestRequests.Should().Be(1);
            (await File.ReadAllBytesAsync(firstOutputPath)).Should().Equal(fixture.Plaintext);
            (await File.ReadAllBytesAsync(secondOutputPath)).Should().Equal(fixture.Plaintext);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain($"SUCCESS 1/2 {fixture.FileName} -> first.bin")
                         .And.Contain($"SUCCESS 2/2 {fixture.FileName} -> second.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldRouteBannerToStandardError_ForDirectDownloadByteOutput()
    {
        var fixture = DownloadHttpFixture.Create();
        using var handler = new SequenceHttpMessageHandler(
            _ => fixture.CreateManifestResponse(),
            _ => fixture.CreateDownloadResponse());
        using var httpClient = new HttpClient(handler);
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "https://shadowdrop.test/d/share-token", "--share-key", fixture.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient,
                                                                       terminalCapabilityProvider: FixedTerminalCapabilityProvider.Plain),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        // The decrypted byte stream stays pure on stdout; the banner is routed to stderr so it never corrupts it.
        binaryOutput.ToArray().Should().Equal(fixture.Plaintext);
        standardOut.ToString().Should().BeEmpty();
        var expectedBanner = new StringWriter();
        await CliBanner.WriteAsync(expectedBanner, FixedTerminalCapabilityProvider.Plain.DetectForStandardError(), CancellationToken.None);
        standardError.ToString().Should().Contain(expectedBanner.ToString());
    }

    [Test]
    public async Task InvokeAsync_ShouldSkipExistingCompletedQueueOutput_WhenLengthAndHashMatch()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var outputPath = Path.Combine(rootDirectory, "payload.bin");
            await File.WriteAllBytesAsync(outputPath, fixture.Plaintext);
            var queuePath = CreateQueueFile(rootDirectory, fixture, ComputeSha256(fixture.Plaintext));
            using var httpClient = new HttpClient(new NeverCalledHandler());
            var standardError = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(new MemoryStream(), new StringWriter(), standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
            standardError.ToString().Should().Contain($"SUCCESS 1/1 {fixture.FileName} -> payload.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldSurfaceAuthorizationFailureAfterRejectedInteractiveBearerToken()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("interactive-protected.bin", 72);
        var upload = await fixture.UploadFilesAsync([inputFile], requireDownloadToken: true);
        var share = upload.Share;
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession();
        interactiveSession.EnqueueTextResponse(upload.ShareKey);
        interactiveSession.EnqueueTextResponse("wrong-token");
        using var httpClient = fixture.CreateClient();

        var exitCode = await CliApplication.InvokeAsync(["download", "--interactive", "--server-url", httpClient.BaseAddress!.ToString(), share.ShareToken],
                                                        CreateServices(binaryOutput,
                                                                       standardOut,
                                                                       standardError,
                                                                       fixture.ConfigFilePath,
                                                                       httpClient: httpClient,
                                                                       interactiveSession: interactiveSession),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Download authorization failed.");
        interactiveSession.TextPrompts.Should().ContainInOrder(("Share key:", true), ("Download bearer token:", true));
        interactiveSession.TextPrompts.Count(prompt => prompt == ("Download bearer token:", true)).Should().Be(1);
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteQueueSuccessLinesBeforeProcessingLaterEntries()
    {
        var fixture = DownloadHttpFixture.Create();
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-command-handler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var firstOutputPath = Path.Combine(rootDirectory, "first.bin");
            var secondOutputPath = Path.Combine(rootDirectory, "second.bin");
            var queuePath = Path.Combine(rootDirectory, "queue.json");
            var queueJson = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files =
                [
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "first.bin"
                    },
                    new()
                    {
                        ServerUrl = "https://shadowdrop.test/base-path/",
                        ShareToken = "shared-token",
                        FileId = fixture.FileId.ToString(),
                        FileName = fixture.FileName,
                        Length = fixture.Plaintext.LongLength,
                        OutputPath = "second.bin"
                    }
                ]
            });
            await File.WriteAllTextAsync(queuePath, queueJson);

            var standardError = new ObservingTextWriter();
            using var handler = new SequenceHttpMessageHandler(
                request =>
                {
                    request.RequestUri.Should().Be(new Uri("https://shadowdrop.test/base-path/d/shared-token"));
                    return fixture.CreateManifestResponse();
                },
                request =>
                {
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/shared-token/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                },
                request =>
                {
                    standardError.Lines.Should().Contain(line => line.StartsWith($"SUCCESS 1/2 {fixture.FileName} -> first.bin"));
                    request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/base-path/d/shared-token/files/{fixture.FileId:D}?mode=cli"));
                    return fixture.CreateDownloadResponse();
                });
            using var httpClient = new HttpClient(handler);
            var binaryOutput = new MemoryStream();
            var standardOut = new StringWriter();

            var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--output-root", rootDirectory, "--share-key", fixture.ShareKey],
                                                            CreateServices(binaryOutput, standardOut, standardError, httpClient: httpClient),
                                                            CancellationToken.None);

            exitCode.Should().Be(0);
            binaryOutput.Length.Should().Be(0);
            (await File.ReadAllBytesAsync(firstOutputPath)).Should().Equal(fixture.Plaintext);
            (await File.ReadAllBytesAsync(secondOutputPath)).Should().Equal(fixture.Plaintext);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain($"SUCCESS 1/2 {fixture.FileName} -> first.bin")
                         .And.Contain($"SUCCESS 2/2 {fixture.FileName} -> second.bin");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [TestCase(null)]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void ParseFileId_ShouldThrow_WhenValueIsInvalid(String? fileId)
    {
        var act = () => DownloadCommandHandler.ParseFileId(fileId);

        act.Should().Throw<DownloadCommandException>().WithMessage("Share metadata invalid or missing.");
    }

    [Test]
    public void SelectDirectDownloadFile_ShouldReturnMatchingFile_WhenFileIdProvided()
    {
        var wanted = Guid.NewGuid();
        var manifest = new ShareManifestContract
        {
            Files =
            [
                new()
                {
                    FileId = Guid.NewGuid().ToString()
                },
                new()
                {
                    FileId = wanted.ToString(),
                    FileName = "wanted.bin"
                }
            ]
        };

        DownloadCommandHandler.SelectDirectDownloadFile(manifest, wanted.ToString()).FileName.Should().Be("wanted.bin");
    }

    [Test]
    public void SelectDirectDownloadFile_ShouldThrow_WhenMultipleFilesAndNoFileId()
    {
        var manifest = new ShareManifestContract
        {
            Files =
            [
                new()
                {
                    FileId = Guid.NewGuid().ToString()
                },
                new()
                {
                    FileId = Guid.NewGuid().ToString()
                }
            ]
        };

        var act = () => DownloadCommandHandler.SelectDirectDownloadFile(manifest, null);

        act.Should().Throw<DownloadCommandException>().WithMessage("Share contains multiple files; specify --file.");
    }

    [Test]
    public void SelectFileById_ShouldThrow_WhenDuplicateFileIds()
    {
        var duplicate = Guid.NewGuid();
        var files = new ShareManifestFileContract[]
        {
            new()
            {
                FileId = duplicate.ToString()
            },
            new()
            {
                FileId = duplicate.ToString()
            }
        };

        var act = () => DownloadCommandHandler.SelectFileById(files, duplicate);

        act.Should().Throw<DownloadCommandException>().WithMessage("Share metadata invalid or missing.");
    }

    [Test]
    public void SelectFileById_ShouldThrow_WhenFileNotFound()
    {
        var act = () => DownloadCommandHandler.SelectFileById([
            new()
            {
                FileId = Guid.NewGuid().ToString()
            }
        ], Guid.NewGuid());

        act.Should().Throw<DownloadCommandException>().WithMessage("Requested file not found in share.");
    }

    private static String ComputeSha256(Byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static String CreateQueueFile(String rootDirectory, DownloadHttpFixture fixture, String? plaintextSha256 = null, params String[] outputPaths) =>
        CreateQueueFileCore(rootDirectory, fixture, "https://shadowdrop.test/base-path/", plaintextSha256, outputPaths);

    private static String CreateQueueFileCore(String rootDirectory,
                                              DownloadHttpFixture fixture,
                                              String serverUrl,
                                              String? plaintextSha256,
                                              params String[] outputPaths)
    {
        var queuePath = Path.Combine(rootDirectory, "queue.json");
        var paths = outputPaths.Length == 0 ? ["payload.bin"] : outputPaths;
        var queueJson = QueueFileParser.Serialize(new()
        {
            ShadowDrop = "1.0",
            QueueVersion = "1.0",
            Files = paths.Select(path => new QueueFileEntry
            {
                ServerUrl = serverUrl,
                ShareToken = "shared-token",
                FileId = fixture.FileId.ToString(),
                FileName = fixture.FileName,
                Length = fixture.Plaintext.LongLength,
                OutputPath = path,
                PlaintextSha256 = plaintextSha256
            }).ToArray()
        });
        File.WriteAllText(queuePath, queueJson);
        return queuePath;
    }

    private static String CreateQueueFileWithServerUrl(String rootDirectory, DownloadHttpFixture fixture, String serverUrl, params String[] outputPaths) =>
        CreateQueueFileCore(rootDirectory, fixture, serverUrl, null, outputPaths);

    private static CliApplicationServices CreateServices(Stream standardOutStream,
                                                         TextWriter standardOut,
                                                         TextWriter standardError,
                                                         String? configPath = null,
                                                         IReadOnlyDictionary<String, String?>? environmentValues = null,
                                                         HttpClient? httpClient = null,
                                                         FakeInteractiveSession? interactiveSession = null,
                                                         ITerminalCapabilityProvider? terminalCapabilityProvider = null) =>
        new(new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(environmentValues ?? new Dictionary<String, String?>())),
            httpClient ?? new HttpClient(new NeverCalledHandler()),
            standardOutStream,
            standardOut,
            standardError,
            interactiveSession ?? new FakeInteractiveSession(),
            TimeProvider.System,
            new PlainDownloadProgressReporterFactory(standardError, TimeProvider.System),
            terminalCapabilityProvider ?? new TerminalCapabilityProvider());

    private static async Task WriteResumeMarkerAsync(String markerPath,
                                                     String serverUrl,
                                                     String shareToken,
                                                     String fileId,
                                                     String? fileName,
                                                     Int64 fileLength,
                                                     String? kdfSalt,
                                                     String? plaintextSha256)
    {
        var json = JsonSerializer.Serialize(new
        {
            version = 1,
            serverUrl,
            shareToken,
            fileId,
            fileName,
            fileLength,
            kdfSalt,
            plaintextSha256
        });
        await File.WriteAllTextAsync(markerPath, json);
    }

    private sealed class CancellingReadStream(Stream inner, Int32 bytesBeforeCancellation, CancellationTokenSource cancellation) : Stream
    {
        private Int32 _bytesRead;

        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => inner.Length;

        public override Int64 Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bytesRead >= bytesBeforeCancellation)
            {
                await cancellation.CancelAsync();
                throw new OperationCanceledException(cancellation.Token);
            }

            var allowed = Math.Min(buffer.Length, bytesBeforeCancellation - _bytesRead);
            var read = await inner.ReadAsync(buffer[..allowed], cancellationToken);
            _bytesRead += read;
            return read;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CliDownloadApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private const String AdminOperationsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnableAdminOperations";
        private const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
        private const String MetadataPathEnvironmentVariable = "ShadowDrop__Metadata__LiteDbPath";
        private const String PublicDownloadsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnablePublicDownloads";
        private const String StorageRootEnvironmentVariable = "ShadowDrop__Storage__LocalRoot";
        private readonly String? _previousAdminOperationsExposure;
        private readonly String? _previousBootstrapToken;
        private readonly String? _previousMetadataPath;
        private readonly String? _previousPublicDownloadsExposure;
        private readonly String? _previousStorageRoot;
        private readonly String _rootDirectory =
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-download-tests", Guid.NewGuid().ToString("N"));

        public CliDownloadApiFactory()
        {
            Directory.CreateDirectory(_rootDirectory);
            ConfigFilePath = Path.Combine(_rootDirectory, ".config", "shadowdrop", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
            _previousBootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable);
            _previousMetadataPath = Environment.GetEnvironmentVariable(MetadataPathEnvironmentVariable);
            _previousStorageRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            _previousAdminOperationsExposure = Environment.GetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable);
            _previousPublicDownloadsExposure = Environment.GetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable);
            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, BootstrapToken);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, MetadataDatabasePath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, StorageRoot);
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, "true");
        }

        public String BootstrapToken { get; } = Convert.ToHexStringLower(Encoding.UTF8.GetBytes($"bootstrap-{Guid.NewGuid():N}"));

        public String ConfigFilePath { get; }

        public String MetadataDatabasePath => Path.Combine(_rootDirectory, "metadata", "shadowdrop.db");

        public String RootDirectory => _rootDirectory;

        public String StorageRoot => Path.Combine(_rootDirectory, "storage");

        public String CreateInputFile(String fileName, Int32 length)
        {
            var path = Path.Combine(_rootDirectory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Enumerable.Range(0, length).Select(static value => (Byte)(value % 251)).ToArray();
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public String CreateQueueFile(QueueFixture fixture)
        {
            var queuePath = Path.Combine(_rootDirectory, $"queue-{Guid.NewGuid():N}.json");
            var json = QueueFileParser.Serialize(new()
            {
                ShadowDrop = "1.0",
                QueueVersion = "1.0",
                Files = fixture.Entries.Select(entry => new QueueFileEntry
                {
                    ServerUrl = fixture.ServerUrl,
                    ShareToken = fixture.ShareToken,
                    FileId = entry.FileId,
                    FileName = entry.FileName,
                    Length = entry.Length,
                    OutputPath = entry.OutputPath
                }).ToArray()
            });
            File.WriteAllText(queuePath, json);
            return queuePath;
        }

        public async Task<ShareFixture> CreateShareAsync(IReadOnlyList<Guid> fileIds, Boolean requireDownloadToken = false)
        {
            using var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", BootstrapToken);
            var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
                                                 fileIds.Select(id => new CreateShareFileRequest(id)).ToArray(),
                                                 false,
                                                 requireDownloadToken,
                                                 requireDownloadToken ? DateTimeOffset.Parse("2026-06-30T12:00:00Z") : null);
            using var response = await client.PostAsJsonAsync("/api/admin/shares", request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreateShareResult>();
            result.Should().NotBeNull();
            return new(result.ShareToken, result.DownloadBearerToken);
        }

        public async Task<UploadFixture> UploadFilesAsync(IReadOnlyList<String> filePaths, Boolean requireDownloadToken = false)
        {
            using var httpClient = CreateClient();
            WriteConfig(httpClient.BaseAddress!.ToString(), BootstrapToken);
            var standardOut = new StringWriter();
            var standardError = new StringWriter();
            var args = new List<String>
            {
                "upload"
            };
            args.AddRange(filePaths);
            if (requireDownloadToken)
            {
                args.Add("--download-token");
            }

            args.Add("--json");
            var exitCode = await CliApplication.InvokeAsync(args.ToArray(),
                                                            CreateServices(Stream.Null, standardOut, standardError, ConfigFilePath, httpClient: httpClient),
                                                            CancellationToken.None);
            exitCode.Should().Be(0, standardError.ToString());
            using var document = JsonDocument.Parse(standardOut.ToString());
            var root = document.RootElement;
            var fileIds = root.GetProperty("uploadedFileIds").EnumerateArray().Select(static element => Guid.Parse(element.GetString()!)).ToArray();
            var shareKey = root.GetProperty("credentials").GetProperty("shareKey").GetString()!;
            var shareToken = root.GetProperty("shareToken").GetString()!;
            var downloadBearerToken = root.GetProperty("credentials").GetProperty("downloadBearerToken").GetString();
            return new(fileIds, shareKey, new(shareToken, downloadBearerToken));
        }

        public void WriteConfig(String serverUrl, String uploadToken) =>
            File.WriteAllText(ConfigFilePath, $"{{\"serverUrl\":\"{serverUrl}\",\"uploadToken\":\"{uploadToken}\"}}");

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

        private async ValueTask DisposeAsyncCore()
        {
            await base.DisposeAsync();
            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, _previousBootstrapToken);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, _previousMetadataPath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, _previousStorageRoot);
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, _previousAdminOperationsExposure);
            Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, _previousPublicDownloadsExposure);
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await DisposeAsyncCore();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class DownloadHttpFixture
    {
        private DownloadHttpFixture(Guid fileId, Byte[] keyMaterial, Byte[] kdfSalt, Byte[] plaintext, Int32 chunkSize, Byte[][] encryptedChunks)
        {
            FileId = fileId;
            KeyMaterial = keyMaterial;
            KdfSalt = kdfSalt;
            Plaintext = plaintext;
            ChunkSize = chunkSize;
            EncryptedChunks = encryptedChunks;
        }

        public Int32 ChunkSize { get; }

        public Byte[][] EncryptedChunks { get; }

        public Guid FileId { get; }

        public String FileName => "payload.bin";

        public Byte[] KdfSalt { get; }

        public Byte[] Plaintext { get; }

        public String ShareKey => Convert.ToHexStringLower(KeyMaterial);

        private Byte[] KeyMaterial { get; }

        public static DownloadHttpFixture Create()
        {
            var fileId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            var keyMaterial = Enumerable.Range(1, 32).Select(static value => (Byte)value).ToArray();
            var kdfSalt = Enumerable.Range(65, 32).Select(static value => (Byte)value).ToArray();
            var plaintext = Enumerable.Range(0, 128).Select(static value => (Byte)(255 - value)).ToArray();
            const Int32 chunkSize = 64;
            using var shareSecret = ShareSecret.FromBytes(keyMaterial);
            var encryptionContext = new FileEncryptionContext(fileId, kdfSalt);
            using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, encryptionContext);
            var encryptedChunks = new List<Byte[]>();
            var chunkCount = plaintext.LongLength / chunkSize;

            for (var chunkIndex = 0L; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkPlaintext = plaintext.Skip(checked((Int32)(chunkIndex * chunkSize))).Take(chunkSize).ToArray();
                var encryptedChunk = ChunkEncryptionService.EncryptChunk(chunkPlaintext,
                                                                         contentKey,
                                                                         new(CryptoVersion.V1,
                                                                             CryptoAlgorithm.Aes256Gcm,
                                                                             fileId,
                                                                             chunkSize,
                                                                             chunkIndex,
                                                                             chunkPlaintext.Length,
                                                                             chunkIndex == chunkCount - 1));
                encryptedChunks.Add(encryptedChunk.Ciphertext);
            }

            return new(fileId, keyMaterial, kdfSalt, plaintext, chunkSize, encryptedChunks.ToArray());
        }

        public HttpResponseMessage CreateCancellingDownloadResponse(Int32 bytesBeforeCancellation, CancellationTokenSource cancellation)
        {
            var response = CreateDownloadResponse();
            var contentLength = response.Content.Headers.ContentLength;
            response.Content = new StreamContent(new CancellingReadStream(response.Content.ReadAsStream(), bytesBeforeCancellation, cancellation));
            response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
            response.Content.Headers.ContentLength = contentLength;
            return response;
        }

        public HttpResponseMessage CreateDownloadResponse(RequestedPlaintextRangeContract? requestedRange = null)
        {
            var range = requestedRange ?? new RequestedPlaintextRangeContract
            {
                Start = 0,
                End = Plaintext.LongLength
            };
            var chunkRange = ChunkEncryptionService.GetChunkRange(range.Start, range.End - range.Start, ChunkSize);
            var responseBody = EncryptedChunks.Skip(checked((Int32)chunkRange.FirstChunkIndex))
                                              .Take(checked((Int32)(chunkRange.LastChunkIndex - chunkRange.FirstChunkIndex + 1)))
                                              .SelectMany(static chunk => chunk)
                                              .ToArray();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBody)
            };
            response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
            response.Content.Headers.ContentLength = responseBody.LongLength;
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FirstChunkIndexHeaderName, chunkRange.FirstChunkIndex.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.LastChunkIndexHeaderName, chunkRange.LastChunkIndex.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeStartHeaderName, range.Start.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeEndHeaderName, range.End.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName, Plaintext.LongLength.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.ChunkSizeHeaderName, ChunkSize.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName, ChunkSize.ToString());
            return response;
        }

        public HttpResponseMessage CreateInterruptedDownloadResponse(Int32 bytesBeforeFailure)
        {
            var response = CreateDownloadResponse();
            var contentLength = response.Content.Headers.ContentLength;
            response.Content = new StreamContent(new InterruptingReadStream(response.Content.ReadAsStream(), bytesBeforeFailure));
            response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
            response.Content.Headers.ContentLength = contentLength;
            return response;
        }

        public ShareManifestFileContract CreateManifestFile(String? fileName = null, String? plaintextSha256 = null) =>
            new()
            {
                AlgorithmId = "aes-256-gcm",
                ChunkCount = EncryptedChunks.Length,
                ChunkSize = ChunkSize,
                EncryptionFormatVersion = "1",
                FileId = FileId.ToString(),
                FileName = fileName ?? FileName,
                KdfSalt = Convert.ToBase64String(KdfSalt),
                Length = Plaintext.LongLength,
                PlaintextSha256 = plaintextSha256
            };

        public HttpResponseMessage CreateManifestResponse(String? fileName = null, String? plaintextSha256 = null)
        {
            return CreateManifestResponseWithFiles(CreateManifestFile(fileName, plaintextSha256));
        }

        public HttpResponseMessage CreateManifestResponseWithFiles(params ShareManifestFileContract[] files)
        {
            var manifest = new ShareManifestContract
            {
                Files = files
            };
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class InterruptingReadStream(Stream inner, Int32 bytesBeforeFailure) : Stream
    {
        private Int32 _bytesRead;

        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => inner.Length;

        public override Int64 Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bytesRead >= bytesBeforeFailure)
            {
                throw new IOException("Simulated interrupted download.");
            }

            var allowed = Math.Min(buffer.Length, bytesBeforeFailure - _bytesRead);
            var read = await inner.ReadAsync(buffer[..allowed], cancellationToken);
            _bytesRead += read;
            return read;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }

    private sealed class ObservingTextWriter : StringWriter
    {
        private readonly List<String> _lines = [];

        public IReadOnlyList<String> Lines => _lines;

        public override Task WriteLineAsync(String? value)
        {
            _lines.Add(value ?? String.Empty);
            return base.WriteLineAsync(value);
        }
    }

    private sealed record QueueFileEntryFixture(String FileId, String FileName, Int64 Length, String OutputPath);

    private sealed record QueueFixture
    {
        public required IReadOnlyList<QueueFileEntryFixture> Entries { get; init; }
        public required String ServerUrl { get; init; }
        public required String ShareToken { get; init; }
    }

    private sealed class SequenceHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new AssertionException("Unexpected extra HTTP request.");
            }

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record ShareFixture(String ShareToken, String? DownloadBearerToken);

    private sealed class StubConfigPathResolver(String? configPath) : CliConfigPathResolver
    {
        public override String? GetConfigFilePath() => configPath;
    }

    private sealed class StubEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => values.TryGetValue(variableName, out var value) ? value : null;
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => throw new NotSupportedException();

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw exception;

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            Task.FromException<Int32>(exception);

        public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<Int32>(exception);

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }

    private sealed record UploadFixture(IReadOnlyList<Guid> FileIds, String ShareKey, ShareFixture Share);
}
