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
using System.Text;

[NonParallelizable]
public sealed class UploadCommandHandlerTests
{
    [Test]
    public async Task InvokeAsync_ShouldFailWithGenericError_WhenServerUrlIsMissing()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var services = new CliApplicationServices(new(new StubConfigPathResolver(null), new StubEnvironmentReader(new Dictionary<String, String?>())),
                                                  httpClient,
                                                  standardOut,
                                                  standardError);

        var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Server URL invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFailWithGenericError_WhenTokenIsMissing()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var services = new CliApplicationServices(new(new StubConfigPathResolver(null), new StubEnvironmentReader(new Dictionary<String, String?>
                                                  {
                                                      ["SHADOWDROP_SERVER_URL"] = "https://shadowdrop.test/"
                                                  })),
                                                  httpClient,
                                                  standardOut,
                                                  standardError);

        var exitCode = await CliApplication.InvokeAsync(["upload", "placeholder.bin"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Authentication token invalid or missing.");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectEmptyFiles_AndNotEmitSecret()
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
            standardError);
        var emptyFile = fixture.CreateInputFile("empty.bin", 0);

        var exitCode = await CliApplication.InvokeAsync(["upload", emptyFile, "--output-secret"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("File 1 failed: File is unreadable.");
        standardError.ToString().Should().NotContain(emptyFile).And.NotContain("secret:");
        fixture.GetStoredUploads().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldReturnNonZeroWithoutSecret_WhenSomeFilesFail()
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
            standardError);
        var okFile = fixture.CreateInputFile("delta.bin", 128);
        var missingFile = Path.Combine(fixture.RootDirectory, "missing.bin");

        var exitCode = await CliApplication.InvokeAsync(["upload", okFile, missingFile, "--output-secret"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().HaveCount(1);
        Guid.Parse(stdoutLines[0]).Should().NotBe(Guid.Empty);
        standardOut.ToString().Should().NotContain("secret:");
        standardError.ToString().Should().Contain("File 2 failed: File is missing.");
        standardError.ToString().Should().NotContain(missingFile);
    }

    [Test]
    public async Task InvokeAsync_ShouldUploadLargeFileAcrossMultipleChunks()
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
            standardError);
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
        var services = new CliApplicationServices(
            new(new StubConfigPathResolver(fixture.ConfigFilePath), new StubEnvironmentReader(new Dictionary<String, String?>())),
            httpClient,
            standardOut,
            standardError);
        var filePath = fixture.CreateInputFile("gamma.bin", 80);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath], services, CancellationToken.None);

        exitCode.Should().Be(0);
        Guid.Parse(standardOut.ToString().Trim()).Should().NotBe(Guid.Empty);
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
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        standardOut.ToString().Should().NotContain("secret:");
        standardError.ToString().Should().NotContain(fixture.BootstrapToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldUseFlagValuesBeforeEnvironmentAndConfig_AndEmitSecretOnlyOnFullSuccess()
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
            ["upload", filePath, "--server-url", httpClient.BaseAddress!.ToString(), "--upload-token", $"  {fixture.BootstrapToken}  ", "--output-secret"],
            services,
            CancellationToken.None);
        exitCode.Should().Be(0);
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().HaveCount(2);
        Guid.Parse(stdoutLines[0]).Should().NotBe(Guid.Empty);
        stdoutLines[1].Should().MatchRegex("^secret:[0-9a-f]{64}$");
        standardOut.ToString().Should().NotContain("wrong-env-token").And.NotContain("wrong-config-token");
        standardError.ToString().Should().NotContain(fixture.BootstrapToken).And.NotContain("wrong-env-token").And.NotContain("wrong-config-token");
        fixture.GetStoredUploads().Should().ContainSingle();
    }

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
            var bytes = Enumerable.Range(0, length).Select(static value => (Byte)(value % 251)).ToArray();
            File.WriteAllBytes(path, bytes);
            return path;
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
