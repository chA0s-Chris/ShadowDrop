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
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Queue;
using ShadowDrop.Tests.Fakes;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

[NonParallelizable]
public sealed class UploadCommandHandlerTests
{
    private const Int64 MultipartEnvelopeAllowanceBytes = 128L * 1024;

    [Test]
    public void DirectHttpCurlCommandFactory_ShouldPosixQuoteHeaderUrlAndFileName()
    {
        var fileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var shareSecretHex = $"fb{new String('f', 62)}";
        var expectedKeyBase64 = Convert.ToBase64String(Convert.FromHexString(shareSecretHex));

        var command = DirectHttpCurlCommandFactory.Create(new("https://shadowdrop.test/root"), "share/token", fileId, shareSecretHex,
                                                          "weird 'name'.bin");

        // Key rides in the header verbatim (single-quoted, so its '+', '/', and '=' need no URL-encoding) and never in the URL.
        command.Should().Contain($"-H 'ShadowDrop-Key: {expectedKeyBase64}'")
               .And.Contain("'https://shadowdrop.test/root/d/share%2Ftoken/files/11111111-2222-3333-4444-555555555555'")
               .And.NotContain("sd-key=")
               // The file name is single-quoted and each embedded quote is escaped as '\''.
               .And.EndWith(@"-o 'weird '\''name'\''.bin'");
    }

    [Test]
    public void DirectHttpDownloadUrlFactory_ShouldUrlEncodeBase64KeyMaterial()
    {
        var fileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var shareSecretHex = $"fb{new String('f', 62)}";

        var downloadUrl = DirectHttpDownloadUrlFactory.Create(new("https://shadowdrop.test/root"), "share/token", fileId, shareSecretHex);

        var uri = new Uri(downloadUrl);
        uri.AbsolutePath.Should().Be("/root/d/share%2Ftoken/files/11111111-2222-3333-4444-555555555555");
        uri.Query.Should().Contain("sd-key=%2B").And.Contain("%2F").And.Contain("%3D");
        Convert.FromBase64String(ReadDirectHttpKeyMaterial(uri)).Should().Equal(Convert.FromHexString(shareSecretHex));
    }

    [Test]
    public async Task InvokeAsync_ShouldApplyDisplayName_ForShareCreateByFileId()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);

        var rawOut = new StringWriter();
        var rawServices = CreateServices(rawOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("raw-source.bin", 48);
        (await CliApplication.InvokeAsync(["upload", "raw", filePath], rawServices, CancellationToken.None)).Should().Be(0);
        var fileId = Value(FindLine(rawOut.ToString(), "file-id:"));

        var shareOut = new StringWriter();
        var shareServices = CreateServices(shareOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        (await CliApplication.InvokeAsync(["share", "create", fileId, "--display-name", $"{fileId}=Share Renamed.bin"],
                                          shareServices, CancellationToken.None)).Should().Be(0);
        var shareUrl = Value(FindLine(shareOut.ToString(), "share-url:"));

        // The manifest (and therefore the queued file name) is public, so the secret-free queue needs only the token.
        var queuePath = Path.Combine(fixture.RootDirectory, "share-created.queue.json");
        var queueServices = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        (await CliApplication.InvokeAsync(["queue", "create", shareUrl, "--out", queuePath],
                                          queueServices, CancellationToken.None)).Should().Be(0);

        ReadQueueFileNames(queuePath).Should().Equal("Share Renamed.bin");
    }

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
        // The banner is written to stderr right before the success output, after the per-file progress lines.
        var expectedBanner = new StringWriter();
        await CliBanner.WriteAsync(expectedBanner, FixedTerminalCapabilityProvider.Plain.DetectForStandardError(), CancellationToken.None);
        var standardErrorText = standardError.ToString();
        standardErrorText.Should()
                         .Contain("START 1/3 first.bin (32 B)")
                         .And.Contain("SUCCESS 1/3 first.bin (32 B/32 B (100.0%)")
                         .And.Contain("START 2/3 second.bin (64 B)")
                         .And.Contain("SUCCESS 2/3 second.bin (64 B/64 B (100.0%)")
                         .And.Contain("START 3/3 third.bin (112 B)")
                         .And.Contain("SUCCESS 3/3 third.bin (112 B/112 B (100.0%)")
                         .And.NotContain("PROGRESS")
                         .And.Contain(expectedBanner.ToString());
        // The banner is emitted after the per-file progress lines, so it must appear after the final SUCCESS line.
        standardErrorText.IndexOf(expectedBanner.ToString(), StringComparison.Ordinal)
                         .Should().BeGreaterThan(standardErrorText.IndexOf("SUCCESS 3/3 third.bin", StringComparison.Ordinal));
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
        standardOut.ToString().Should().NotContain("download-url:");
        standardOut.ToString().Should().NotContain("download-bearer-token:");
        standardError.ToString().Should().Contain("SUCCESS 1/1 alpha.bin (144 B/144 B (100.0%)")
                     .And.NotContain("PROGRESS")
                     .And.NotContain(fixture.BootstrapToken);
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
    public async Task InvokeAsync_ShouldEmitCurlCommandInJson_WhenDirectHttpRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-curl-json.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--json"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var directHttpDownload = document.RootElement.GetProperty("directHttpDownloads").EnumerateArray().Should().ContainSingle().Subject;
        var curlCommand = directHttpDownload.GetProperty("curlCommand").GetString()!;
        curlCommand.Should().StartWith("curl -H 'ShadowDrop-Key: ")
                   .And.Contain($"/files/{directHttpDownload.GetProperty("fileId").GetString()}")
                   .And.NotContain("sd-key=");
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitDirectHttpDownloadsInJson_WhenDirectHttpRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-json.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--json"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(standardOut.ToString());
        var root = document.RootElement;
        root.GetProperty("shareUrl").GetString().Should().StartWith($"{httpClient.BaseAddress}d/");
        var shareKey = root.GetProperty("credentials").GetProperty("shareKey").GetString()!;
        var directHttpDownload = root.GetProperty("directHttpDownloads").EnumerateArray().Should().ContainSingle().Subject;
        directHttpDownload.GetProperty("fileId").GetString().Should().Be(root.GetProperty("uploadedFileIds")[0].GetString());
        directHttpDownload.GetProperty("fileName").GetString().Should().Be("direct-json.bin");
        var downloadUrl = directHttpDownload.GetProperty("downloadUrl").GetString()!;
        downloadUrl.Should().StartWith($"{httpClient.BaseAddress}d/")
                   .And.Contain("/files/")
                   .And.Contain("sd-key=");
        Convert.FromBase64String(ReadDirectHttpKeyMaterial(new Uri(downloadUrl))).Should().Equal(Convert.FromHexString(shareKey));
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitDirectHttpDownloadUrl_ForSingleFile()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-single.bin", 128);
        var plaintext = await File.ReadAllBytesAsync(filePath);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        FindLine(standardOut.ToString(), "share-url:").Should().StartWith($"share-url:{httpClient.BaseAddress}d/");
        var downloadUrl = Value(FindLine(standardOut.ToString(), "download-url:"));
        var storedUpload = fixture.GetStoredUploads().Should().ContainSingle().Subject;
        var downloadUri = new Uri(downloadUrl);
        downloadUri.AbsoluteUri.Should().StartWith($"{httpClient.BaseAddress}d/");
        downloadUri.AbsolutePath.Should().EndWith($"/files/{storedUpload.FileId:D}");
        downloadUri.Query.Should().Contain("sd-key=").And.Contain("%3D");
        standardOut.ToString().Should().NotContain("share-key:");

        using var response = await httpClient.GetAsync(downloadUri, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsByteArrayAsync(CancellationToken.None)).Should().Equal(plaintext);
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitHeaderBasedCurlCommand_ForSingleFile()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-curl.bin", 128);
        var plaintext = await File.ReadAllBytesAsync(filePath);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var command = Value(FindLine(standardOut.ToString(), "curl-command:"));
        var storedUpload = fixture.GetStoredUploads().Should().ContainSingle().Subject;
        var (headerKeyMaterial, url) = ParseCurlHeaderAndUrl(command);
        command.Should().NotContain("sd-key=");
        new Uri(url).AbsolutePath.Should().EndWith($"/files/{storedUpload.FileId:D}");

        // The emitted command really retrieves the file via the header, with no key in the URL.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("ShadowDrop-Key", headerKeyMaterial);
        using var response = await httpClient.SendAsync(request, CancellationToken.None);
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsByteArrayAsync(CancellationToken.None)).Should().Equal(plaintext);
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
        root.TryGetProperty("directHttpDownloads", out _).Should().BeFalse();
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitOneCurlCommandPerFile_ForMultipleFiles()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePaths = new[]
        {
            fixture.CreateInputFile("curl-first.bin", 16),
            fixture.CreateInputFile("curl-second.bin", 48),
            fixture.CreateInputFile("curl-third.bin", 96)
        };

        var exitCode = await CliApplication.InvokeAsync(["upload", "--direct-http", ..filePaths], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = FindLines(standardOut.ToString(), "curl-command:");
        lines.Should().HaveCount(3);
        var storedFileIds = fixture.GetStoredUploads().Select(static upload => upload.FileId.ToString()).ToHashSet(StringComparer.Ordinal);
        var parsed = lines.Select(ParseMultiFileCurlLine).ToArray();
        parsed.Select(static entry => entry.FileId).Should().OnlyContain(fileId => storedFileIds.Contains(fileId));
        parsed.Select(static entry => entry.Command).Should()
              .OnlyContain(command => command.StartsWith("curl -H 'ShadowDrop-Key: ", StringComparison.Ordinal)
                                      && !command.Contains("sd-key=", StringComparison.Ordinal));
    }

    [Test]
    public async Task InvokeAsync_ShouldEmitOneDirectHttpDownloadUrlPerFile_ForMultipleFiles()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePaths = new[]
        {
            fixture.CreateInputFile("direct-first.bin", 16),
            fixture.CreateInputFile("direct-second.bin", 48),
            fixture.CreateInputFile("direct-third.bin", 96)
        };

        var exitCode = await CliApplication.InvokeAsync(["upload", "--direct-http", ..filePaths], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = FindLines(standardOut.ToString(), "download-url:");
        lines.Should().HaveCount(3);
        var storedFileIds = fixture.GetStoredUploads().Select(static upload => upload.FileId.ToString()).ToHashSet(StringComparer.Ordinal);
        var parsedDownloads = lines.Select(ParseMultiFileDownloadLine).ToArray();
        parsedDownloads.Select(static download => download.FileId).Should().OnlyContain(fileId => storedFileIds.Contains(fileId));
        parsedDownloads.Select(static download => download.DownloadUrl).Should()
                       .OnlyContain(url => new Uri(url).Query.Contains("sd-key=", StringComparison.Ordinal));
        standardOut.ToString().Should().NotContain("share-key:");
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenDisplayNameMappingIsUnknown()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("present.bin", 16);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--display-name", "absent.bin=Name.bin"],
                                                        services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("No file matches");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldFail_WhenNameUsedWithMultipleFiles()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var firstPath = fixture.CreateInputFile("multi-first.bin", 16);
        var secondPath = fixture.CreateInputFile("multi-second.bin", 16);

        var exitCode = await CliApplication.InvokeAsync(["upload", firstPath, secondPath, "--name", "Only One.bin"],
                                                        services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--name option requires exactly one file");
        // The input contract is rejected before any upload happens.
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldFailBeforeUploading_WhenServerDoesNotExposeUploadLimit()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new UploadLimitUnavailableHandler());
        fixture.WriteConfig("https://shadowdrop.test/", fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("payload.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Upload limit could not be resolved from the server");
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
        standardError.ToString().Should().Contain("Authentication token invalid or missing.")
                     .And.NotContain("File 1 failed");
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
    public async Task InvokeAsync_ShouldFallBackToOriginalName_ForUnmappedFiles()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var firstPath = fixture.CreateInputFile("kept.bin", 16);
        var secondPath = fixture.CreateInputFile("renamed.bin", 32);
        var queuePath = Path.Combine(fixture.RootDirectory, "partial.queue.json");

        var exitCode = await CliApplication.InvokeAsync(
            ["upload", firstPath, secondPath, "--display-name", $"{secondPath}=Renamed Display.bin", "--queue-out", queuePath],
            services, CancellationToken.None);

        exitCode.Should().Be(0);
        ReadQueueFileNames(queuePath).Should().BeEquivalentTo("kept.bin", "Renamed Display.bin");
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
        standardError.ToString().Should().Contain("FAILED 2/2 missing.bin: File is missing.").And.NotContain(missingFile);
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
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
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

        var downloadPath = Path.Combine(fixture.RootDirectory, "roundtrip-download.bin");
        var downloadExit = await CliApplication.InvokeAsync(["download", shareUrl, "--share-key", shareKey, "--out", downloadPath], services,
                                                            CancellationToken.None);

        downloadExit.Should().Be(0);
        (await File.ReadAllBytesAsync(downloadPath)).Should().Equal(plaintext);
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
        standardError.ToString().Should().Contain("FAILED 1/1 empty.bin: File is empty.");
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
    public async Task InvokeAsync_ShouldRejectOversizedMultiFileBatchBeforeUploadingAnyFile()
    {
        var uploadMaxBytes = MultipartEnvelopeAllowanceBytes + 80;
        await using var fixture = new CliUploadApiFactory(uploadMaxBytes);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var smallPath = fixture.CreateInputFile("small.bin", 16);
        var oversizedPath = fixture.CreateInputFile("oversized.bin", 96);
        var hugePath = fixture.CreateInputFile("huge.bin", 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", smallPath, oversizedPath, hugePath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("FAILED 2/3 oversized.bin")
                     .And.Contain("oversized.bin")
                     .And.Contain("FAILED 3/3 huge.bin")
                     .And.Contain("huge.bin")
                     .And.Contain("maximum is 80 bytes")
                     .And.NotContain("START");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectOversizedSingleFileBeforeUploading()
    {
        var uploadMaxBytes = MultipartEnvelopeAllowanceBytes + 80;
        await using var fixture = new CliUploadApiFactory(uploadMaxBytes);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("oversized.bin", 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("oversized.bin")
                     .And.Contain("Upload size is 144 bytes; maximum is 80 bytes.")
                     .And.NotContain("START");
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
    public async Task InvokeAsync_ShouldRejectSecretsOutForDirectHttpShare()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-secret.bin", 32);
        var secretsPath = Path.Combine(fixture.RootDirectory, "direct-secrets.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--secrets-out", secretsPath], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Direct HTTP shares do not support writing secrets to a separate file");
        fixture.GetStoredUploads().Should().BeEmpty();
        File.Exists(secretsPath).Should().BeFalse();
    }

    [Test]
    public async Task InvokeAsync_ShouldReportOversizedFileDetailsInJson()
    {
        var uploadMaxBytes = MultipartEnvelopeAllowanceBytes + 80;
        await using var fixture = new CliUploadApiFactory(uploadMaxBytes);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("oversized-json.bin", 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        using var result = JsonDocument.Parse(standardOut.ToString());
        var root = result.RootElement;
        root.GetProperty("status").GetString().Should().Be("upload-failed");
        root.GetProperty("uploadedFileIds").GetArrayLength().Should().Be(0);
        var failure = root.GetProperty("failures").EnumerateArray().Should().ContainSingle().Subject;
        failure.GetProperty("fileNumber").GetInt32().Should().Be(1);
        failure.GetProperty("fileName").GetString().Should().Be("oversized-json.bin");
        failure.GetProperty("message").GetString().Should().Contain("maximum upload size");
        failure.GetProperty("uploadSizeBytes").GetInt64().Should().Be(144);
        failure.GetProperty("maxFilePayloadBytes").GetInt64().Should().Be(80);
        standardError.ToString().Should().BeEmpty();
        fixture.GetStoredUploads().Should().BeEmpty();
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
    public async Task InvokeAsync_ShouldRouteBannerToStandardError_WhenJsonRequested()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("json-banner.bin", 96);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--json"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        // The banner must never corrupt the JSON stdout contract: stdout stays parseable JSON while the banner is
        // routed to stderr instead.
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("status").GetString().Should().Be("succeeded");
        var expectedBanner = new StringWriter();
        await CliBanner.WriteAsync(expectedBanner, FixedTerminalCapabilityProvider.Plain.DetectForStandardError(), CancellationToken.None);
        standardOut.ToString().Should().NotContain(expectedBanner.ToString());
        standardError.ToString().Should().Contain(expectedBanner.ToString());
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
        standardError.ToString().Should().Contain("SUCCESS 1/1 --data.bin");
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
        standardError.ToString().Should().Contain("SUCCESS 1/1 large.bin")
                     .And.Contain("(100.0%)")
                     .And.NotContain("PROGRESS");
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
    public async Task InvokeAsync_ShouldUseDisplayName_InDirectHttpCurlCommand()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("direct-on-disk.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--direct-http", "--name", "Direct Name.bin"],
                                                        services, CancellationToken.None);

        exitCode.Should().Be(0);
        Value(FindLine(standardOut.ToString(), "curl-command:")).Should().EndWith("-o 'Direct Name.bin'");
    }

    [Test]
    public async Task InvokeAsync_ShouldUseDisplayName_InQueueOutput_ForSingleFileName()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("on-disk.bin", 64);
        var queuePath = Path.Combine(fixture.RootDirectory, "named.queue.json");

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--name", "Recipient Name.bin", "--queue-out", queuePath],
                                                        services, CancellationToken.None);

        exitCode.Should().Be(0);
        ReadQueueFileNames(queuePath).Should().Equal("Recipient Name.bin");
    }

    [Test]
    public async Task InvokeAsync_ShouldUseDisplayNames_InQueueOutput_ForMultiFileMapping()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var firstPath = fixture.CreateInputFile("first.bin", 16);
        var secondPath = fixture.CreateInputFile("second.bin", 32);
        var queuePath = Path.Combine(fixture.RootDirectory, "mapped.queue.json");

        var exitCode = await CliApplication.InvokeAsync(
            [
                "upload", firstPath, secondPath, "--display-name", $"{firstPath}=One.bin", "--display-name", $"{secondPath}=Two.bin",
                "--queue-out", queuePath
            ],
            services, CancellationToken.None);

        exitCode.Should().Be(0);
        ReadQueueFileNames(queuePath).Should().BeEquivalentTo("One.bin", "Two.bin");
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
        standardError.ToString().Should().Contain("SUCCESS 1/1 alpha.bin");
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
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig(httpClient.BaseAddress!.ToString(), fixture.BootstrapToken);
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
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

        var downloadPath = Path.Combine(fixture.RootDirectory, "composed-download.bin");
        var downloadExit = await CliApplication.InvokeAsync(["download", shareUrl, "--share-key", shareKey, "--out", downloadPath], services,
                                                            CancellationToken.None);

        downloadExit.Should().Be(0);
        (await File.ReadAllBytesAsync(downloadPath)).Should().Equal(plaintext);
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
    public async Task Upload_ShouldEmitSingleRetryLineThenSucceed_WhenTransientUploadFailureRetries()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new TransientThenSuccessUploadHandler();
        using var httpClient = new HttpClient(handler);
        fixture.WriteConfig("https://shadowdrop.test/", fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var filePath = fixture.CreateInputFile("retry.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        handler.UploadRequests.Should().Be(2);
        var errorOutput = standardError.ToString();
        // A transient 503 restarts request streaming for the same file: exactly one deterministic retry line, then one success.
        errorOutput.Should().Contain("START 1/1 retry.bin")
                   .And.Contain("RETRY 1/1 retry.bin attempt 2")
                   .And.Contain("SUCCESS 1/1 retry.bin (80 B/80 B (100.0%)");
        errorOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                   .Count(static line => line.StartsWith("RETRY", StringComparison.Ordinal)).Should().Be(1);
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
    public async Task UploadRaw_ShouldRejectMissingFilesBeforeUploadingBatch()
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
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("FAILED 2/2 missing.bin: File is missing.");
        fixture.GetStoredUploads().Should().BeEmpty();
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
        standardError.ToString().Should().Contain("SUCCESS 1/1 raw.bin (80 B/80 B (100.0%)")
                     .And.NotContain("PROGRESS");
        fixture.GetStoredUploads().Should().ContainSingle();
    }

    [Test]
    public async Task UploadRaw_ShouldReportRuntimeFailureDetailsInJson()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new RuntimeFailureUploadHandler();
        using var httpClient = new HttpClient(handler);
        fixture.WriteConfig("https://shadowdrop.test/", fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var firstFile = fixture.CreateInputFile("raw-first.bin", 64);
        var secondFile = fixture.CreateInputFile("raw-second.bin", 64);
        var thirdFile = fixture.CreateInputFile("raw-third.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", firstFile, secondFile, thirdFile, "--json"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        using var result = JsonDocument.Parse(standardOut.ToString());
        var root = result.RootElement;
        root.GetProperty("status").GetString().Should().Be("upload-failed");
        root.GetProperty("uploadedFileIds").GetArrayLength().Should().Be(1);
        root.GetProperty("credentials").ValueKind.Should().Be(JsonValueKind.Null);
        var failure = root.GetProperty("failures").EnumerateArray().Should().ContainSingle().Subject;
        failure.GetProperty("fileNumber").GetInt32().Should().Be(2);
        failure.GetProperty("fileName").GetString().Should().Be("raw-second.bin");
        failure.GetProperty("message").GetString().Should().Be("Upload failed; please verify file and try again.");
        failure.TryGetProperty("uploadSizeBytes", out _).Should().BeFalse();
        failure.TryGetProperty("maxFilePayloadBytes", out _).Should().BeFalse();
        standardError.ToString().Should().BeEmpty();
        handler.UploadRequests.Should().Be(2);
    }

    [Test]
    public async Task UploadRaw_ShouldStopRemainingBatchAndReportSucceededFileIds_WhenRuntimeUploadFails()
    {
        await using var fixture = new CliUploadApiFactory();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new RuntimeFailureUploadHandler();
        using var httpClient = new HttpClient(handler);
        fixture.WriteConfig("https://shadowdrop.test/", fixture.BootstrapToken);
        var services = CreateServices(standardOut, standardError, fixture.ConfigFilePath, httpClient: httpClient);
        var firstFile = fixture.CreateInputFile("first.bin", 64);
        var secondFile = fixture.CreateInputFile("second.bin", 64);
        var thirdFile = fixture.CreateInputFile("third.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", "raw", firstFile, secondFile, thirdFile], services, CancellationToken.None);

        exitCode.Should().Be(1);
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().ContainSingle().Which.Should().StartWith("file-id:");
        Guid.Parse(Value(stdoutLines[0])).Should().NotBe(Guid.Empty);
        standardOut.ToString().Should().NotContain("share-key:");
        standardError.ToString().Should().Contain("SUCCESS 1/3 first.bin")
                     .And.Contain("FAILED 2/3 second.bin: Upload failed; please verify file and try again.")
                     .And.NotContain("third.bin");
        handler.CapabilitiesRequests.Should().Be(1);
        handler.ReservationRequests.Should().Be(2);
        handler.UploadRequests.Should().Be(2);
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
                   _ => resolvedHttpClient,
                   standardOut,
                   standardError,
                   interactiveSession ?? new FakeInteractiveSession(),
                   TimeProvider.System,
                   new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
                   FixedTerminalCapabilityProvider.Plain);
    }

    private static String? FindLine(String standardOut, String prefix) =>
        standardOut.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                   .SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static IReadOnlyList<String> FindLines(String standardOut, String prefix) =>
        standardOut.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                   .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                   .ToArray();

    private static (String HeaderKeyMaterial, String Url) ParseCurlHeaderAndUrl(String command)
    {
        // Format: curl -H '<header>' '<url>' -o '<file-name>'. The header value and URL contain no single quotes,
        // so splitting on the single-quote delimiter yields them at the odd indices.
        var segments = command.Split('\'');
        var headerArgument = segments[1];
        const String headerPrefix = "ShadowDrop-Key: ";
        return (headerArgument[headerPrefix.Length..], segments[3]);
    }

    private static (String FileId, String Command) ParseMultiFileCurlLine(String line)
    {
        // The file ID has no colon, so the first colon of the payload separates it from the command (which may contain colons).
        var payload = Value(line);
        var separatorIndex = payload.IndexOf(':', StringComparison.Ordinal);
        return (payload[..separatorIndex], payload[(separatorIndex + 1)..]);
    }

    private static (String FileId, String DownloadUrl) ParseMultiFileDownloadLine(String line)
    {
        var payload = Value(line);
        var separatorIndex = payload.IndexOf(':', StringComparison.Ordinal);
        return (payload[..separatorIndex], payload[(separatorIndex + 1)..]);
    }

    private static String ReadDirectHttpKeyMaterial(Uri downloadUri)
    {
        var query = downloadUri.Query.TrimStart('?');
        var keyPrefix = "sd-key=";
        query.Should().StartWith(keyPrefix);
        return Uri.UnescapeDataString(query[keyPrefix.Length..]);
    }

    private static IReadOnlyList<String> ReadQueueFileNames(String queuePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(queuePath));
        return document.RootElement.GetProperty("files")
                       .EnumerateArray()
                       .Select(static file => file.GetProperty("fileName").GetString()!)
                       .ToArray();
    }

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
        private const String UploadMaxBytesEnvironmentVariable = "ShadowDrop__Upload__MaxBytes";
        private readonly String? _previousAdminOperationsExposure;
        private readonly String? _previousBootstrapToken;
        private readonly String? _previousMetadataPath;
        private readonly String? _previousStorageRoot;
        private readonly String? _previousUploadMaxBytes;
        private readonly String _rootDirectory =
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-upload-tests", Guid.NewGuid().ToString("N"));

        public CliUploadApiFactory(Int64? uploadMaxBytes = null)
        {
            Directory.CreateDirectory(_rootDirectory);
            ConfigFilePath = Path.Combine(_rootDirectory, ".config", "shadowdrop", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
            WriteConfig("http://localhost/", "wrong-config-token");
            _previousBootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable);
            _previousMetadataPath = Environment.GetEnvironmentVariable(MetadataPathEnvironmentVariable);
            _previousStorageRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            _previousAdminOperationsExposure = Environment.GetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable);
            _previousUploadMaxBytes = Environment.GetEnvironmentVariable(UploadMaxBytesEnvironmentVariable);
            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, BootstrapToken);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, MetadataDatabasePath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, StorageRoot);
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, "true");
            Environment.SetEnvironmentVariable(UploadMaxBytesEnvironmentVariable, uploadMaxBytes?.ToString(CultureInfo.InvariantCulture));
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
            Environment.SetEnvironmentVariable(UploadMaxBytesEnvironmentVariable, _previousUploadMaxBytes);
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

    private sealed class RuntimeFailureUploadHandler : HttpMessageHandler
    {
        private readonly Queue<Guid> _reservedFileIds = new();

        public Int32 CapabilitiesRequests { get; private set; }

        public Int32 ReservationRequests { get; private set; }

        public Int32 UploadRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/admin/uploads/capabilities")
            {
                CapabilitiesRequests++;
                return CreateJsonResponse(HttpStatusCode.OK, """{"maxFilePayloadBytes":4096}""");
            }

            if (request.Method == HttpMethod.Post && path == "/api/admin/uploads/reservations")
            {
                ReservationRequests++;
                var fileId = Guid.NewGuid();
                _reservedFileIds.Enqueue(fileId);
                return CreateJsonResponse(HttpStatusCode.Created, $$"""{"fileId":"{{fileId}}"}""");
            }

            if (request.Method == HttpMethod.Post && path == "/api/admin/uploads")
            {
                UploadRequests++;
                if (UploadRequests == 1)
                {
                    await request.Content!.CopyToAsync(Stream.Null, cancellationToken);
                    return CreateJsonResponse(HttpStatusCode.Created, $$"""{"fileId":"{{_reservedFileIds.Dequeue()}}"}""");
                }

                return new(HttpStatusCode.BadRequest);
            }

            throw new AssertionException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, String json) =>
            new(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed class StubConfigPathResolver(String? configPath) : CliConfigPathResolver
    {
        public override String? GetConfigFilePath() => configPath;
    }

    private sealed class StubEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => values.TryGetValue(variableName, out var value) ? value : null;
    }

    private sealed class TransientThenSuccessUploadHandler : HttpMessageHandler
    {
        private readonly Queue<Guid> _reservedFileIds = new();

        public Int32 UploadRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/admin/uploads/capabilities")
            {
                return CreateJsonResponse(HttpStatusCode.OK, """{"maxFilePayloadBytes":4096}""");
            }

            if (request.Method == HttpMethod.Post && path == "/api/admin/uploads/reservations")
            {
                var fileId = Guid.NewGuid();
                _reservedFileIds.Enqueue(fileId);
                return CreateJsonResponse(HttpStatusCode.Created, $$"""{"fileId":"{{fileId}}"}""");
            }

            if (request.Method == HttpMethod.Post && path == "/api/admin/uploads")
            {
                UploadRequests++;
                if (UploadRequests == 1)
                {
                    // Transient failure: SendWithRetryAsync recreates the request content and retries the same file.
                    return new(HttpStatusCode.ServiceUnavailable);
                }

                await request.Content!.CopyToAsync(Stream.Null, cancellationToken);
                return CreateJsonResponse(HttpStatusCode.Created, $$"""{"fileId":"{{_reservedFileIds.Dequeue()}}"}""");
            }

            throw new AssertionException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, String json) =>
            new(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed class UploadLimitUnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.Should().Be("/api/admin/uploads/capabilities");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
