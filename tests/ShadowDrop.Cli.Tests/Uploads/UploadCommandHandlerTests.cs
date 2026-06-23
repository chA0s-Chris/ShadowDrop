// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Queue;
using ShadowDrop.Tests.Fakes;
using System.Text;
using System.Text.Json;

[NonParallelizable]
public sealed class UploadCommandHandlerTests
{
    [Test]
    public async Task InvokeAsync_ShouldCreateOneShareForMultipleFiles_AndNotEmitFileIds()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePaths = new[]
        {
            fixture.CreateInputFile("first.bin", 16),
            fixture.CreateInputFile("second.bin", 48),
            fixture.CreateInputFile("third.bin", 96)
        };

        var exitCode = await CliApplication.InvokeAsync(["upload", ..filePaths], services, CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                   .Should().HaveCount(2)
                   .And.OnlyContain(line => line.StartsWith("share-url:", StringComparison.Ordinal)
                                            || line.StartsWith("share-key:", StringComparison.Ordinal));
        standardError.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                     .Should().Equal("Uploaded file 1 of 3.", "Uploaded file 2 of 3.", "Uploaded file 3 of 3.");
        fixture.GetStoredUploads().Should().HaveCount(3);
    }

    [Test]
    public async Task InvokeAsync_ShouldCreateShareAndDeliverCredentialsOnStdout_ForSingleFileSuccess()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("alpha.bin", 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-url:").Should().StartWith($"share-url:{httpClient.BaseAddress}d/");
        Value(FindLine(standardOut.ToString(), "share-key:")).Should().MatchRegex("^[0-9a-f]{64}$");
        standardOut.ToString().Should().NotContain("download-bearer-token:");
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.").And.NotContain(fixture.BootstrapToken);
        fixture.GetStoredUploads().Should().ContainSingle();
    }

    [Test]
    public async Task InvokeAsync_ShouldDownloadViaEmbeddedQueue_WithoutSeparateCredentials()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("roundtrip.bin", 256);
        var plaintext = await File.ReadAllBytesAsync(filePath);
        var queuePath = Path.Combine(fixture.RootDirectory, "selfcontained.queue.json");

        var uploadExit = await CliApplication.InvokeAsync(["upload", filePath, "--queue-out", queuePath, "--embed-secrets"], services, CancellationToken.None);
        uploadExit.Should().Be(0);

        var outputDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputDirectory);
        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(outputDirectory);
        try
        {
            var downloadExit = await CliApplication.InvokeAsync(["download", "--queue", queuePath], services, CancellationToken.None);
            downloadExit.Should().Be(0);
            (await File.ReadAllBytesAsync(Path.Combine(outputDirectory, "roundtrip.bin"))).Should().Equal(plaintext);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldEmbedCredentialsInQueue_WhenEmbedSecretsRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("embedded.bin", 96);
        var queuePath = Path.Combine(fixture.RootDirectory, "embedded.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--queue-out", queuePath, "--embed-secrets", "--json"], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var expectedShareKey = document.RootElement.GetProperty("credentials").GetProperty("shareKey").GetString();
        document.RootElement.GetProperty("queueFile").GetString().Should().Be(queuePath);
        var queue = QueueFileParser.Parse(await File.ReadAllTextAsync(queuePath));
        queue.Credentials.Should().NotBeNull();
        queue.Credentials!.ShareKey.Should().Be(expectedShareKey);
        if (OperatingSystem.IsLinux())
        {
            File.GetUnixFileMode(queuePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitJsonResultWithCredentials_WhenJsonRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("json.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.GetProperty("status").GetString().Should().Be("succeeded");
        root.GetProperty("uploadedFileIds").GetArrayLength().Should().Be(1);
        root.GetProperty("shareId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("shareToken").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("shareUrl").GetString().Should().StartWith($"{httpClient.BaseAddress}d/");
        root.GetProperty("credentials").GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        root.GetProperty("secretsFile").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task InvokeAsync_ShouldFailCleanly_WhenConfigFileContainsMalformedJson()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-upload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var configPath = Path.Combine(rootDirectory, ".config", "shadowdrop", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, "{\"serverUrl\":");
            var services = new CliApplicationServices(
                new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(new Dictionary<String, String?>())),
                httpClient,
                standardOut,
                standardError);

            var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"], services, CancellationToken.None);

            exitCode.Should().Be(1);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain("Configuration file invalid or unreadable.");
            standardError.ToString().Should().NotContain("JsonException").And.NotContain(configPath);
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
    public async Task InvokeAsync_ShouldFailCleanly_WhenConfigFileIsUnreadable()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-upload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var configPath = Path.Combine(rootDirectory, ".config", "shadowdrop", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, "{\"serverUrl\":\"https://shadowdrop.test/\",\"uploadToken\":\"token\"}");
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("Unreadable file permissions test is only supported on Linux.");
            }

            SetUnreadableFileMode(configPath);
            var services = new CliApplicationServices(
                new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(new Dictionary<String, String?>())),
                httpClient,
                standardOut,
                standardError);

            var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"], services, CancellationToken.None);

            exitCode.Should().Be(1);
            standardOut.ToString().Should().BeEmpty();
            standardError.ToString().Should().Contain("Configuration file invalid or unreadable.");
            standardError.ToString().Should().NotContain(configPath);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                var configPath = Path.Combine(rootDirectory, ".config", "shadowdrop", "config.json");
                if (File.Exists(configPath) && OperatingSystem.IsLinux())
                {
                    RestoreReadableFileMode(configPath);
                }

                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldFailCleanly_WhenUploadTokenIsWrongAgainstRealApi()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), "wrong-token");
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
            standardOut,
            standardError);
        var filePath = fixture.CreateInputFile("wrong-token.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("File 1 failed: Authentication token invalid or missing.");
        standardError.ToString().Should().NotContain("wrong-token")
                     .And.NotContain(httpClient.BaseAddress!.ToString())
                     .And.NotContain(filePath);
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldFailWithGenericError_WhenServerUrlIsMissing()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Server URL invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFailWithGenericError_WhenTokenIsMissing()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"],
                                                        CreateServices(standardOut,
                                                                       standardError,
                                                                       environmentValues: new Dictionary<String, String?>
                                                                       {
                                                                           ["SHADOWDROP_SERVER_URL"] = "https://shadowdrop.test/"
                                                                       }),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Authentication token invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldGenerateDownloadBearerToken_WhenRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("tokened.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--download-token"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        Value(FindLine(standardOut.ToString(), "share-key:")).Should().MatchRegex("^[0-9a-f]{64}$");
        Value(FindLine(standardOut.ToString(), "download-bearer-token:")).Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task InvokeAsync_ShouldGuideInteractiveUploadAndKeepSecretsOutOfDiagnostics()
    {
        await using var fixture = new CliUploadApiFactory();
        var inputFile = fixture.CreateInputFile("interactive.bin", 96);
        using var httpClient = fixture.CreateClient();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession();
        interactiveSession.EnqueueSelection(2); // Expiration: 7 days
        interactiveSession.EnqueueConfirmation(false); // Enable direct HTTP downloads?
        interactiveSession.EnqueueConfirmation(true); // Require a download bearer token?

        var exitCode = await CliApplication.InvokeAsync(
            ["upload", "--interactive", "--server-url", httpClient.BaseAddress!.ToString(), "--upload-token", fixture.BootstrapToken, inputFile],
            CreateServices(standardOut, standardError, httpClient: httpClient, interactiveSession: interactiveSession),
            CancellationToken.None);

        exitCode.Should().Be(0);
        // Interactive delegates to the shared upload handler, so the output matches the non-interactive command.
        var shareKey = Value(FindLine(standardOut.ToString(), "share-key:"));
        shareKey.Should().MatchRegex("^[0-9a-f]{64}$");
        FindLine(standardOut.ToString(), "share-url:").Should().StartWith($"share-url:{httpClient.BaseAddress}d/");
        var bearerTokenLine = FindLine(standardOut.ToString(), "download-bearer-token:")!;
        standardError.ToString().Should().NotContain(shareKey)
                     .And.NotContain(bearerTokenLine)
                     .And.NotContain(fixture.BootstrapToken);
        interactiveSession.Summaries.Should().Contain(summary => summary.Title == "Upload plan"
                                                                 && summary.Rows.Any(row => row.Label == "Download bearer token"
                                                                                            && row.Value == "Required"));
    }

    [Test]
    public async Task InvokeAsync_ShouldHonorInteractiveUploadOutputOptions_WhenDelegating()
    {
        await using var fixture = new CliUploadApiFactory();
        var inputFile = fixture.CreateInputFile("interactive-secrets.bin", 96);
        var secretsPath = Path.Combine(fixture.RootDirectory, "interactive-creds.json");
        await File.WriteAllTextAsync(secretsPath, "existing");
        using var httpClient = fixture.CreateClient();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession();
        interactiveSession.EnqueueSelection(2); // Expiration: 7 days
        interactiveSession.EnqueueConfirmation(false); // Enable direct HTTP downloads?
        interactiveSession.EnqueueConfirmation(false); // Require a download bearer token?

        var exitCode = await CliApplication.InvokeAsync(
            [
                "upload",
                "--interactive",
                "--server-url",
                httpClient.BaseAddress!.ToString(),
                "--upload-token",
                fixture.BootstrapToken,
                "--secrets-out",
                secretsPath,
                "--json",
                "--force",
                inputFile
            ],
            CreateServices(standardOut, standardError, httpClient: httpClient, interactiveSession: interactiveSession),
            CancellationToken.None);

        exitCode.Should().Be(0);
        using var result = JsonDocument.Parse(standardOut.ToString());
        result.RootElement.GetProperty("credentials").ValueKind.Should().Be(JsonValueKind.Null);
        result.RootElement.GetProperty("secretsFile").GetString().Should().Be(secretsPath);
        standardOut.ToString().Should().NotContain("share-key:")
                   .And.NotContain("shareKey");

        using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        credentials.RootElement.GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        standardError.ToString().Should().NotContain(fixture.BootstrapToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldNotCreateShare_WhenSomeFilesFail()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var okFile = fixture.CreateInputFile("delta.bin", 128);
        var missingFile = Path.Combine(fixture.RootDirectory, "missing.bin");

        var exitCode = await CliApplication.InvokeAsync(["upload", okFile, missingFile], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("File 2 failed: File is missing.").And.NotContain(missingFile);
    }

    [Test]
    public async Task InvokeAsync_ShouldOverwriteSecretsFile_WithForce()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("overwrite-force.bin", 64);
        var secretsPath = Path.Combine(fixture.RootDirectory, "force-creds.json");
        await File.WriteAllTextAsync(secretsPath, "preexisting");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--secrets-out", secretsPath, "--force"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        document.RootElement.GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task InvokeAsync_ShouldPointSecretFreeQueueNoteAtSecretsFile_WhenSecretsOutProvided()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("queued.bin", 96);
        var queuePath = Path.Combine(fixture.RootDirectory, "out.queue.json");
        var secretsPath = Path.Combine(fixture.RootDirectory, "creds.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--queue-out", queuePath, "--secrets-out", secretsPath], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        // The share key is in the secrets file, not printed, so the note must point there rather than at 'share-key:' output.
        FindLine(standardOut.ToString(), "share-key:").Should().BeNull();
        standardError.ToString().Should().Contain("secret-free").And.Contain(secretsPath).And.Contain("--embed-secrets");
    }

    [Test]
    public async Task InvokeAsync_ShouldProduceUsableDownloadCapability_EndToEnd()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var binaryOut = new MemoryStream();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
            binaryOut,
            standardOut,
            standardError,
            new FakeInteractiveSession(),
            TimeProvider.System);
        var filePath = fixture.CreateInputFile("roundtrip.bin", 256);
        var plaintext = await File.ReadAllBytesAsync(filePath);

        var uploadExit = await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None);
        uploadExit.Should().Be(0);

        using var result = JsonDocument.Parse(standardOut.ToString());
        var shareUrl = result.RootElement.GetProperty("shareUrl").GetString()!;
        var shareKey = result.RootElement.GetProperty("credentials").GetProperty("shareKey").GetString()!;

        var downloadExit = await CliApplication.InvokeAsync(["download", shareUrl, "--share-key", shareKey], services, CancellationToken.None);

        downloadExit.Should().Be(0);
        binaryOut.ToArray().Should().Equal(plaintext);
    }

    [Test]
    public async Task InvokeAsync_ShouldRefuseToOverwriteSecretsFile_WithoutForce()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("overwrite.bin", 64);
        var secretsPath = Path.Combine(fixture.RootDirectory, "existing-creds.json");
        await File.WriteAllTextAsync(secretsPath, "preexisting");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--secrets-out", secretsPath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Refusing to overwrite an existing file. Pass --force to overwrite.");
        (await File.ReadAllTextAsync(secretsPath)).Should().Be("preexisting");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectDirectHttpWithDownloadToken_BeforeUploading()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("conflict.bin", 32);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--download-token"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Direct HTTP shares cannot generate a download bearer token.");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectEmbedSecretsWithoutQueueOut()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("noqueue.bin", 32);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--embed-secrets"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--embed-secrets requires --queue-out.");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectEmptyFiles_AndNotCreateShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var emptyFile = fixture.CreateInputFile("empty.bin", 0);

        var exitCode = await CliApplication.InvokeAsync(["upload", emptyFile], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("File 1 failed: File is empty.");
        standardError.ToString().Should().NotContain(emptyFile).And.NotContain("share-key:");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectExplicitCredentials_WhenQueueAlreadyEmbedsThem()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("conflict.bin", 96);
        var queuePath = Path.Combine(fixture.RootDirectory, "conflict.queue.json");
        (await CliApplication.InvokeAsync(["upload", filePath, "--queue-out", queuePath, "--embed-secrets"], services, CancellationToken.None)).Should().Be(0);

        var conflictError = new StringWriter();
        var downloadServices = CreateServices(new(), conflictError, fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", new('a', 64)], downloadServices,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        conflictError.ToString().Should().Contain("The queue already contains credentials");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectInvalidExpiration_BeforeUploading()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("badexp.bin", 32);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--expires-in", "soon"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Share expiration invalid.");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectQueueOutForDirectHttpShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct.bin", 32);
        var queuePath = Path.Combine(fixture.RootDirectory, "direct.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--queue-out", queuePath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Direct HTTP shares do not support queue generation");
        fixture.GetStoredUploads().Should().BeEmpty();
        File.Exists(queuePath).Should().BeFalse();
    }

    [Test]
    public async Task InvokeAsync_ShouldReportSecretsFilePathInJson_WhenSecretsOutAndJsonCombined()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("json-secrets.bin", 96);
        var secretsPath = Path.Combine(fixture.RootDirectory, "json-creds.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--json", "--secrets-out", secretsPath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.GetProperty("credentials").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("secretsFile").GetString().Should().Be(secretsPath);
        using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        credentials.RootElement.GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task InvokeAsync_ShouldStoreEncryptedBlobAndNonSecretMetadataOnly()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile(Path.Combine("nested", "secret-notes.bin"), 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var shareSecret = Value(FindLine(standardOut.ToString(), "share-key:"));
        var storedUpload = fixture.GetStoredUploads().Should().ContainSingle().Subject;
        var storedBlobPath = fixture.GetBlobPath(storedUpload.FileId);
        var storedBlob = await File.ReadAllBytesAsync(storedBlobPath);
        var plaintext = await File.ReadAllBytesAsync(filePath);

        storedBlob.Should().NotEqual(plaintext);
        storedBlob.Length.Should().Be((Int32)storedUpload.EncryptedLength);
        storedUpload.OriginalFileName.Should().Be("secret-notes.bin");
        storedUpload.OriginalFileName.Should().NotContain(fixture.RootDirectory).And.NotContain(Path.DirectorySeparatorChar.ToString());
        storedUpload.BlobKey.Should().NotContain(fixture.RootDirectory).And.NotContain("secret-notes.bin");
        storedUpload.KdfSaltBase64.Should().NotBe(shareSecret);
        storedUpload.PlaintextSha256.Should().BeNull();
        standardError.ToString().Should().NotContain(shareSecret).And.NotContain(filePath);
    }

    [Test]
    public async Task InvokeAsync_ShouldTreatDashedFileNamesAsOperandsAfterSeparator()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("--data.bin", 24);

        var exitCode = await CliApplication.InvokeAsync(["upload", "--", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-url:").Should().NotBeNull();
        fixture.GetStoredUploads().Should().ContainSingle(record => record.OriginalFileName == "--data.bin");
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
    }

    [Test]
    public async Task InvokeAsync_ShouldUploadLargeFileAcrossMultipleChunks()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var plaintextLength = (1024 * 1024) + 33;
        var filePath = fixture.CreateInputFile("large.bin", plaintextLength);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var storedUpload = fixture.GetStoredUploads().Should().ContainSingle().Subject;
        storedUpload.PlaintextLength.Should().Be(plaintextLength);
        storedUpload.ChunkCount.Should().Be(2);
        storedUpload.EncryptedLength.Should().Be(plaintextLength + (storedUpload.ChunkCount * 16));
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
    }

    [Test]
    public async Task InvokeAsync_ShouldUseConfigWhenFlagsAndEnvironmentAreMissing()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("gamma.bin", 80);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-url:").Should().NotBeNull();
        standardError.ToString().Should().NotContain(fixture.BootstrapToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldUseEnvironmentWhenFlagsAreMissing_AndTrimToken()
    {
        await using var fixture = new CliUploadApiFactory();
        using var httpClient = fixture.CreateClient();
        var environmentReader = new StubEnvironmentReader(new Dictionary<String, String?>
        {
            ["SHADOWDROP_SERVER_URL"] = httpClient.BaseAddress!.ToString(),
            ["SHADOWDROP_UPLOAD_TOKEN"] = $"  {fixture.BootstrapToken}  "
        });
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), "wrong-config-token");
        var services = new CliApplicationServices(new(new StubConfigPathResolver(fixture.ConfigFilePath), environmentReader), httpClient, standardOut,
                                                  standardError);
        var filePath = fixture.CreateInputFile("beta.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-key:").Should().NotBeNull();
        standardError.ToString().Should().NotContain(fixture.BootstrapToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldUseFlagValuesBeforeEnvironmentAndConfig_AndDeliverSecretsOnlyOnFullSuccess()
    {
        await using var fixture = new CliUploadApiFactory();
        var configPathResolver = new StubConfigPathResolver(fixture.ConfigFilePath);
        var environmentReader = new StubEnvironmentReader(new Dictionary<String, String?>
        {
            ["SHADOWDROP_SERVER_URL"] = "https://unused.invalid/",
            ["SHADOWDROP_UPLOAD_TOKEN"] = " wrong-env-token "
        });
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), "wrong-config-token");
        var services = new CliApplicationServices(new(configPathResolver, environmentReader), httpClient, standardOut, standardError);
        var filePath = fixture.CreateInputFile("alpha.bin", 128);

        var exitCode = await CliApplication.InvokeAsync(
            ["upload", filePath, "--server-url", httpClient.BaseAddress!.ToString(), "--upload-token", $"  {fixture.BootstrapToken}  "],
            services,
            CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
        Value(FindLine(standardOut.ToString(), "share-key:")).Should().MatchRegex("^[0-9a-f]{64}$");
        standardOut.ToString().Should().NotContain("wrong-env-token").And.NotContain("wrong-config-token");
        standardError.ToString().Should().NotContain(fixture.BootstrapToken).And.NotContain("wrong-env-token").And.NotContain("wrong-config-token");
        fixture.GetStoredUploads().Should().ContainSingle();
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteCredentialsToFile_WhenSecretsOutProvided()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("secrets-out.bin", 64);
        var secretsPath = Path.Combine(fixture.RootDirectory, "creds.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--secrets-out", secretsPath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-url:").Should().NotBeNull();
        FindLine(standardOut.ToString(), "secrets-file:").Should().NotBeNull();
        standardOut.ToString().Should().NotContain("share-key:");
        File.Exists(secretsPath).Should().BeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        document.RootElement.GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        if (OperatingSystem.IsLinux())
        {
            File.GetUnixFileMode(secretsPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteParseFailuresToStandardError_WhenFilesOperandIsMissingAfterOptions()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--server-url", "https://shadowdrop.test/"], CreateServices(standardOut, standardError),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Required argument missing for command: 'upload'.");
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteRootHelpToStdout()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["--help"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Should().Contain("ShadowDrop CLI")
                   .And.Contain("upload")
                   .And.Contain("download")
                   .And.Contain("queue")
                   .And.Contain("share")
                   .And.Contain("--help");
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteSecretFreeQueue_WhenQueueOutProvided()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("queued.bin", 96);
        var queuePath = Path.Combine(fixture.RootDirectory, "out.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--queue-out", queuePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-key:").Should().NotBeNull();
        FindLine(standardOut.ToString(), "queue-file:").Should().Be($"queue-file:{queuePath}");
        standardError.ToString().Should().Contain("secret-free").And.Contain("shown above").And.Contain("--embed-secrets");
        var queue = QueueFileParser.Parse(await File.ReadAllTextAsync(queuePath));
        queue.Credentials.Should().BeNull();
        var entry = queue.Files.Should().ContainSingle().Subject;
        entry.OutputPath.Should().Be("queued.bin");
        entry.ShareToken.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteSubcommandHelpToStdout_ForLowerLevelCommands()
    {
        var rawOut = new StringWriter();
        (await CliApplication.InvokeAsync(["upload", "raw", "--help"], CreateServices(rawOut, new()), CancellationToken.None)).Should().Be(0);
        rawOut.ToString().Should().Contain("without creating a share").And.Contain("--secrets-out");

        var shareOut = new StringWriter();
        (await CliApplication.InvokeAsync(["share", "create", "--help"], CreateServices(shareOut, new()), CancellationToken.None)).Should().Be(0);
        shareOut.ToString().Should().Contain("file-ids").And.Contain("--download-token");

        var queueOut = new StringWriter();
        (await CliApplication.InvokeAsync(["queue", "create", "--help"], CreateServices(queueOut, new()), CancellationToken.None)).Should().Be(0);
        queueOut.ToString().Should().Contain("--out").And.Contain("--embed-secrets");
    }

    [Test]
    public async Task InvokeAsync_ShouldWriteUploadHelpToStdout()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--help"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Should().Contain("Encrypt local files and upload encrypted content to ShadowDrop.")
                   .And.Contain("files")
                   .And.Contain("--server-url")
                   .And.Contain("--upload-token")
                   .And.Contain("--expires-in")
                   .And.Contain("--secrets-out")
                   .And.Contain("--json")
                   .And.Contain("--interactive");
    }

    [Test]
    public async Task LowerLevel_ShouldComposeUploadRawShareCreateAndDownload()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var binaryOut = new MemoryStream();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
            binaryOut,
            standardOut,
            standardError,
            new FakeInteractiveSession(),
            TimeProvider.System);
        var filePath = fixture.CreateInputFile("composed.bin", 256);
        var plaintext = await File.ReadAllBytesAsync(filePath);

        (await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var rawResult = JsonDocument.Parse(standardOut.ToString());
        var fileId = rawResult.RootElement.GetProperty("uploadedFileIds")[0].GetString()!;
        var shareKey = rawResult.RootElement.GetProperty("credentials").GetProperty("shareKey").GetString()!;

        var createOut = new StringWriter();
        var createServices = CreateServices(createOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        (await CliApplication.InvokeAsync(["share", "create", fileId, "--json"], createServices, CancellationToken.None)).Should().Be(0);
        using var shareResult = JsonDocument.Parse(createOut.ToString());
        var shareUrl = shareResult.RootElement.GetProperty("shareUrl").GetString()!;

        var downloadExit = await CliApplication.InvokeAsync(["download", shareUrl, "--share-key", shareKey], services, CancellationToken.None);

        downloadExit.Should().Be(0);
        binaryOut.ToArray().Should().Equal(plaintext);
    }

    [Test]
    public async Task QueueCreate_ShouldEmbedCredentials_WhenShareKeyProvided()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("embed-create.bin", 96);
        (await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var uploadResult = JsonDocument.Parse(standardOut.ToString());
        var shareUrl = uploadResult.RootElement.GetProperty("shareUrl").GetString()!;
        var shareKey = uploadResult.RootElement.GetProperty("credentials").GetProperty("shareKey").GetString()!;
        var queuePath = Path.Combine(fixture.RootDirectory, "embed-create.queue.json");

        var createServices = CreateServices(new(), new(), fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(["queue", "create", shareUrl, "--out", queuePath, "--embed-secrets", "--share-key", shareKey],
                                                        createServices, CancellationToken.None);

        exitCode.Should().Be(0);
        var queue = QueueFileParser.Parse(await File.ReadAllTextAsync(queuePath));
        queue.Credentials!.ShareKey.Should().Be(shareKey);
    }

    [Test]
    public async Task QueueCreate_ShouldRefuseToOverwriteExistingQueue_WithoutForce()
    {
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError,
                                      environmentValues: new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_SERVER_URL"] = "https://shadowdrop.test/"
                                      });
        var queuePath = Path.Combine(Path.GetTempPath(), $"existing-{Guid.NewGuid():N}.queue.json");
        await File.WriteAllTextAsync(queuePath, "preexisting");

        try
        {
            var exitCode = await CliApplication.InvokeAsync(["queue", "create", "some-token", "--out", queuePath], services, CancellationToken.None);

            exitCode.Should().Be(1);
            standardError.ToString().Should().Contain("Refusing to overwrite an existing file. Pass --force to overwrite.");
            (await File.ReadAllTextAsync(queuePath)).Should().Be("preexisting");
        }
        finally
        {
            File.Delete(queuePath);
        }
    }

    [Test]
    public async Task QueueCreate_ShouldRejectEmbedSecretsWithoutShareKey()
    {
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError,
                                      environmentValues: new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_SERVER_URL"] = "https://shadowdrop.test/"
                                      });
        var queuePath = Path.Combine(Path.GetTempPath(), $"reject-{Guid.NewGuid():N}.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["queue", "create", "some-token", "--out", queuePath, "--embed-secrets"], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--embed-secrets requires a share key");
        File.Exists(queuePath).Should().BeFalse();
    }

    [Test]
    public async Task QueueCreate_ShouldReportShareTokenError_ForNonShareUrl()
    {
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError,
                                      environmentValues: new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_SERVER_URL"] = "https://shadowdrop.test/"
                                      });
        var queuePath = Path.Combine(Path.GetTempPath(), $"nonshare-{Guid.NewGuid():N}.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["queue", "create", "https://example.com/not-a-share", "--out", queuePath], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Share token invalid or missing.");
        File.Exists(queuePath).Should().BeFalse();
    }

    [Test]
    public async Task QueueCreate_ShouldWriteSecretFreeQueueFromExistingShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("share-me.bin", 96);
        (await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var uploadResult = JsonDocument.Parse(standardOut.ToString());
        var shareUrl = uploadResult.RootElement.GetProperty("shareUrl").GetString()!;
        var queuePath = Path.Combine(fixture.RootDirectory, "created.queue.json");

        var createOut = new StringWriter();
        var createError = new StringWriter();
        var createServices = CreateServices(createOut, createError, fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(["queue", "create", shareUrl, "--out", queuePath], createServices, CancellationToken.None);

        exitCode.Should().Be(0);
        createOut.ToString().Should().Contain($"queue-file:{queuePath}");
        var queue = QueueFileParser.Parse(await File.ReadAllTextAsync(queuePath));
        queue.Credentials.Should().BeNull();
        queue.Files.Should().ContainSingle(entry => entry.OutputPath == "share-me.bin");
    }

    [Test]
    public async Task ShareCreate_ShouldCreateShareFromUploadedFileIds()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("for-share.bin", 64);
        (await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var rawResult = JsonDocument.Parse(standardOut.ToString());
        var fileId = rawResult.RootElement.GetProperty("uploadedFileIds")[0].GetString()!;

        var createOut = new StringWriter();
        var createServices = CreateServices(createOut, new(), fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(["share", "create", fileId, "--json"], createServices, CancellationToken.None);

        exitCode.Should().Be(0);
        using var shareResult = JsonDocument.Parse(createOut.ToString());
        shareResult.RootElement.GetProperty("status").GetString().Should().Be("succeeded");
        shareResult.RootElement.GetProperty("shareUrl").GetString().Should().StartWith($"{httpClient.BaseAddress}d/");
        shareResult.RootElement.GetProperty("shareToken").GetString().Should().NotBeNullOrWhiteSpace();
        shareResult.RootElement.GetProperty("uploadedFileIds")[0].GetString().Should().Be(fileId);
    }

    [Test]
    public async Task ShareCreate_ShouldFail_WhenNoFileIdsProvided()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["share", "create"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().NotBeEmpty();
    }

    [Test]
    public async Task ShareCreate_ShouldNormalizeFileIdsInJsonResult()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("normalize.bin", 64);
        (await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var rawResult = JsonDocument.Parse(standardOut.ToString());
        var canonicalFileId = rawResult.RootElement.GetProperty("uploadedFileIds")[0].GetString()!;

        var createOut = new StringWriter();
        var createServices = CreateServices(createOut, new(), fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(["share", "create", canonicalFileId.ToUpperInvariant(), "--json"], createServices,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        using var shareResult = JsonDocument.Parse(createOut.ToString());
        shareResult.RootElement.GetProperty("uploadedFileIds")[0].GetString().Should().Be(canonicalFileId);
    }

    [Test]
    public async Task ShareCreate_ShouldRejectInvalidFileId()
    {
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError);

        var exitCode = await CliApplication.InvokeAsync(["share", "create", "not-a-guid"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("File id invalid or missing.");
    }

    [Test]
    public async Task ShareCreate_ShouldRejectSecretsOutWithoutDownloadToken()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(new(), standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var secretsPath = Path.Combine(fixture.RootDirectory, "no-token.json");

        var exitCode = await CliApplication.InvokeAsync(
            ["share", "create", Guid.NewGuid().ToString(), "--secrets-out", secretsPath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--secrets-out requires --download-token");
        File.Exists(secretsPath).Should().BeFalse();
    }

    [Test]
    public async Task ShareCreate_ShouldReportShareUrlOnStdout_WhenSecretsFileWriteFails()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("share-fail.bin", 64);
        (await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var rawResult = JsonDocument.Parse(standardOut.ToString());
        var fileId = rawResult.RootElement.GetProperty("uploadedFileIds")[0].GetString()!;

        // Point --secrets-out at an existing directory so EnsureWritable passes but the atomic move fails after the share is created.
        var secretsDirectory = Path.Combine(fixture.RootDirectory, "creds-dir");
        Directory.CreateDirectory(secretsDirectory);
        var createOut = new StringWriter();
        var createError = new StringWriter();
        var createServices = CreateServices(createOut, createError, fixture.ConfigFilePath, httpClient: httpClient);

        var exitCode = await CliApplication.InvokeAsync(
            ["share", "create", fileId, "--download-token", "--secrets-out", secretsDirectory], createServices, CancellationToken.None);

        exitCode.Should().Be(1);
        FindLine(createOut.ToString(), "share-url:").Should().StartWith($"share-url:{httpClient.BaseAddress}d/");
        createOut.ToString().Should().NotContain("download-bearer-token:").And.NotContain("secrets-file:");
        createError.ToString().Should().Contain("The share was created but its download bearer token could not be delivered.");
    }

    [Test]
    public async Task ShareCreate_ShouldWriteBearerTokenToSecretsFile()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("share-secrets.bin", 64);
        (await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None)).Should().Be(0);
        using var rawResult = JsonDocument.Parse(standardOut.ToString());
        var fileId = rawResult.RootElement.GetProperty("uploadedFileIds")[0].GetString()!;
        var secretsPath = Path.Combine(fixture.RootDirectory, "bearer-creds.json");

        var createOut = new StringWriter();
        var createServices = CreateServices(createOut, new(), fixture.ConfigFilePath, httpClient: httpClient);
        var exitCode = await CliApplication.InvokeAsync(
            ["share", "create", fileId, "--download-token", "--secrets-out", secretsPath], createServices, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(createOut.ToString(), "share-url:").Should().NotBeNull();
        FindLine(createOut.ToString(), "secrets-file:").Should().Be($"secrets-file:{secretsPath}");
        createOut.ToString().Should().NotContain("download-bearer-token:");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        document.RootElement.GetProperty("downloadBearerToken").GetString().Should().NotBeNullOrWhiteSpace();
        if (OperatingSystem.IsLinux())
        {
            File.GetUnixFileMode(secretsPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task UploadRaw_ShouldEmitJsonResultWithoutShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("raw-json.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", filePath, "--json"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.GetProperty("status").GetString().Should().Be("succeeded");
        root.GetProperty("uploadedFileIds").GetArrayLength().Should().Be(1);
        root.GetProperty("shareId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("shareToken").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("shareUrl").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("credentials").GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task UploadRaw_ShouldFail_WhenNoFilesProvided()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().NotBeEmpty();
    }

    [Test]
    public async Task UploadRaw_ShouldReportFileIdsAndShareKey_WithoutCreatingShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("raw.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var fileIdLine = FindLine(standardOut.ToString(), "file-id:");
        Guid.Parse(Value(fileIdLine)).Should().NotBe(Guid.Empty);
        Value(FindLine(standardOut.ToString(), "share-key:")).Should().MatchRegex("^[0-9a-f]{64}$");
        standardOut.ToString().Should().NotContain("share-url:");
        fixture.GetStoredUploads().Should().ContainSingle();
    }

    [Test]
    public async Task UploadRaw_ShouldReportSucceededFileIdsOnStdout_OnPartialFailure()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var okFile = fixture.CreateInputFile("ok.bin", 64);
        var missingFile = Path.Combine(fixture.RootDirectory, "missing.bin");

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", okFile, missingFile], services, CancellationToken.None);

        exitCode.Should().Be(1);
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().ContainSingle().Which.Should().StartWith("file-id:");
        Guid.Parse(Value(stdoutLines[0])).Should().NotBe(Guid.Empty);
        standardOut.ToString().Should().NotContain("share-key:");
        standardError.ToString().Should().Contain("File 2 failed: File is missing.");
    }

    [Test]
    public async Task UploadRaw_ShouldWriteShareKeyToSecretsFile()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("raw-secrets.bin", 64);
        var secretsPath = Path.Combine(fixture.RootDirectory, "raw-creds.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", filePath, "--secrets-out", secretsPath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "file-id:").Should().NotBeNull();
        FindLine(standardOut.ToString(), "secrets-file:").Should().Be($"secrets-file:{secretsPath}");
        standardOut.ToString().Should().NotContain("share-key:");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(secretsPath));
        document.RootElement.GetProperty("shareKey").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        if (OperatingSystem.IsLinux())
        {
            File.GetUnixFileMode(secretsPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         String? configPath = null,
                                                         IReadOnlyDictionary<String, String?>? environmentValues = null,
                                                         HttpClient? httpClient = null,
                                                         FakeInteractiveSession? interactiveSession = null)
    {
        var resolvedHttpClient = httpClient ?? new HttpClient(new NeverCalledHandler());
        return new(new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(environmentValues ?? new Dictionary<String, String?>())),
                   resolvedHttpClient,
                   Stream.Null,
                   standardOut,
                   standardError,
                   interactiveSession ?? new FakeInteractiveSession(),
                   TimeProvider.System);
    }

    private static String? FindLine(String standardOut, String prefix) =>
        standardOut.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                   .SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static void RestoreReadableFileMode(String path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void SetUnreadableFileMode(String path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserWrite);
        }
    }

    private static String Value(String? line) => line!.Split(':', 2)[1];

    private sealed class CliUploadApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private const String AdminOperationsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnableAdminOperations";
        private const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
        private const String MetadataPathEnvironmentVariable = "ShadowDrop__Metadata__LiteDbPath";
        private const String StorageRootEnvironmentVariable = "ShadowDrop__Storage__LocalRoot";
        private readonly String? _previousAdminOperationsExposure;
        private readonly String? _previousBootstrapToken;
        private readonly String? _previousMetadataPath;
        private readonly String? _previousStorageRoot;
        private readonly String _rootDirectory =
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-upload-tests", Guid.NewGuid().ToString("N"));

        public CliUploadApiFactory()
        {
            Directory.CreateDirectory(_rootDirectory);
            ConfigFilePath = Path.Combine(_rootDirectory, ".config", "shadowdrop", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
            WriteConfig("http://localhost/", "wrong-config-token");
            _previousBootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable);
            _previousMetadataPath = Environment.GetEnvironmentVariable(MetadataPathEnvironmentVariable);
            _previousStorageRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            _previousAdminOperationsExposure = Environment.GetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable);
            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, BootstrapToken);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, MetadataDatabasePath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, StorageRoot);
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, "true");
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

        public String GetBlobPath(Guid fileId)
        {
            var blobPath = Directory.EnumerateFiles(StorageRoot, $"{fileId:N}.blob", SearchOption.AllDirectories).SingleOrDefault();
            if (blobPath is not null)
            {
                return blobPath;
            }

            return Directory.EnumerateFiles(StorageRoot, $"{fileId}.blob", SearchOption.AllDirectories).Single();
        }

        public IReadOnlyList<UploadedFileRecord> GetStoredUploads()
        {
            using var scope = Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IUploadedFileMetadataRepository>();
            var ids = Directory.Exists(StorageRoot)
                ? Directory.EnumerateFiles(StorageRoot, "*.blob", SearchOption.AllDirectories)
                           .Select(path => Guid.Parse(Path.GetFileNameWithoutExtension(path)))
                           .ToArray()
                : [];
            return ids.Select(id => repository.GetAsync(id, CancellationToken.None).GetAwaiter().GetResult()!)
                      .Where(static record => record is not null)
                      .ToArray();
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

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }

    private sealed class StubConfigPathResolver(String? configPath) : CliConfigPathResolver
    {
        public override String? GetConfigFilePath() => configPath;
    }

    private sealed class StubEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => values.TryGetValue(variableName, out var value) ? value : null;
    }
}
