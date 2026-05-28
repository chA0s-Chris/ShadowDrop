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
using ShadowDrop.Queue;
using System.Net.Http.Json;
using System.Text;

[NonParallelizable]
public sealed class DownloadCommandHandlerTests
{
    [Test]
    public async Task InvokeAsync_ShouldContinueQueueProcessingAfterIndividualFailures()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("stable.bin", 72);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = await fixture.CreateShareAsync(upload.FileIds);
        using var httpClient = fixture.CreateClient();
        var outputsDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputsDirectory);
        var queuePath = fixture.CreateQueueFile(new()
        {
            ShareId = share.ShareToken,
            ServerUrl = httpClient.BaseAddress!.ToString(),
            Entries =
            [
                new(upload.FileIds[0].ToString(), "stable.bin", 72, Path.Combine(outputsDirectory, "stable.bin")),
                new(Guid.NewGuid().ToString(), "missing.bin", 88, Path.Combine(outputsDirectory, "missing.bin"))
            ]
        });
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", upload.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath,
                                                                       httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "stable.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(inputFile));
        File.Exists(Path.Combine(outputsDirectory, "missing.bin")).Should().BeFalse();
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("SUCCESS stable.bin ->")
                     .And.Contain("FAILED missing.bin ->")
                     .And.Contain("Requested file not found in share.");
    }

    [Test]
    public async Task InvokeAsync_ShouldDownloadSingleFileToStdout_WhenCliShareKeyOverridesKeyFile()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("report.bin", 128);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = await fixture.CreateShareAsync(upload.FileIds);
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
        standardError.ToString().Should().BeEmpty();
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
    public async Task InvokeAsync_ShouldHonorBearerTokenArgument_ForProtectedShares()
    {
        await using var fixture = new CliDownloadApiFactory();
        var inputFile = fixture.CreateInputFile("protected.bin", 96);
        var upload = await fixture.UploadFilesAsync([inputFile]);
        var share = await fixture.CreateShareAsync(upload.FileIds, true);
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
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldProcessQueueAndWritePerFileSummaryToStderr()
    {
        await using var fixture = new CliDownloadApiFactory();
        var firstInput = fixture.CreateInputFile("alpha.bin", 64);
        var secondInput = fixture.CreateInputFile("beta.bin", 80);
        var upload = await fixture.UploadFilesAsync([firstInput, secondInput]);
        var share = await fixture.CreateShareAsync(upload.FileIds);
        using var httpClient = fixture.CreateClient();
        var outputsDirectory = Path.Combine(fixture.RootDirectory, "downloads");
        Directory.CreateDirectory(outputsDirectory);
        var queuePath = fixture.CreateQueueFile(new()
        {
            ShareId = share.ShareToken,
            ServerUrl = httpClient.BaseAddress!.ToString(),
            Entries =
            [
                new(upload.FileIds[0].ToString(), "alpha.bin", 64, Path.Combine(outputsDirectory, "alpha.bin")),
                new(upload.FileIds[1].ToString(), "beta.bin", 80, Path.Combine(outputsDirectory, "beta.bin"))
            ]
        });
        var binaryOutput = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["download", "--queue", queuePath, "--share-key", upload.ShareKey],
                                                        CreateServices(binaryOutput, standardOut, standardError, fixture.ConfigFilePath,
                                                                       httpClient: httpClient),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "alpha.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(firstInput));
        (await File.ReadAllBytesAsync(Path.Combine(outputsDirectory, "beta.bin"))).Should().BeEquivalentTo(await File.ReadAllBytesAsync(secondInput));
        binaryOutput.Length.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("SUCCESS alpha.bin ->")
                     .And.Contain("SUCCESS beta.bin ->");
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
                                           "shareId": "share-123",
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

    private static CliApplicationServices CreateServices(Stream standardOutStream,
                                                         TextWriter standardOut,
                                                         TextWriter standardError,
                                                         String? configPath = null,
                                                         IReadOnlyDictionary<String, String?>? environmentValues = null,
                                                         HttpClient? httpClient = null) =>
        new(new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(environmentValues ?? new Dictionary<String, String?>())),
            httpClient ?? new HttpClient(new NeverCalledHandler()),
            standardOutStream,
            standardOut,
            standardError);

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
                    ShareId = fixture.ShareId,
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
            return new(result!.ShareToken, result.DownloadBearerToken);
        }

        public async Task<UploadFixture> UploadFilesAsync(IReadOnlyList<String> filePaths)
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
            args.Add("--output-secret");
            var exitCode = await CliApplication.InvokeAsync(args.ToArray(),
                                                            CreateServices(Stream.Null, standardOut, standardError, ConfigFilePath, httpClient: httpClient),
                                                            CancellationToken.None);
            exitCode.Should().Be(0, standardError.ToString());
            var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            lines.Should().NotBeEmpty();
            var shareKey = lines[^1];
            shareKey.Should().StartWith("secret:");
            return new(lines[..^1].Select(Guid.Parse).ToArray(), shareKey["secret:".Length..]);
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

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }

    private sealed record QueueFileEntryFixture(String FileId, String FileName, Int64 Length, String OutputPath);

    private sealed record QueueFixture
    {
        public required IReadOnlyList<QueueFileEntryFixture> Entries { get; init; }
        public required String ServerUrl { get; init; }
        public required String ShareId { get; init; }
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

    private sealed record UploadFixture(IReadOnlyList<Guid> FileIds, String ShareKey);
}
