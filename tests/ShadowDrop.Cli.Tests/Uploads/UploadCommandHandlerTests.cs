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
    public async Task InvokeAsync_ShouldIgnoreWhitespaceOverridesWhenResolvingEachConfigurationField()
    {
        await using var fixture = new CliUploadApiFactory();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig("https://config.invalid/", "config-token");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut,
                                      standardError,
                                      fixture.ConfigFilePath,
                                      new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_SERVER_URL"] = httpClient.BaseAddress!.ToString(),
                                          ["SHADOWDROP_UPLOAD_TOKEN"] = fixture.BootstrapToken
                                      },
                                      httpClient);
        var filePath = fixture.CreateInputFile("whitespace-overrides.bin", 72);

        var exitCode = await CliApplication.InvokeAsync(
            ["upload", filePath, "--server-url", "   ", "--upload-token", "   "],
            services,
            CancellationToken.None);

        exitCode.Should().Be(0);
        Guid.Parse(standardOut.ToString().Trim()).Should().NotBe(Guid.Empty);
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
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
    public async Task InvokeAsync_ShouldResolveServerUrlAndTokenIndependentlyAcrossSources()
    {
        await using var fixture = new CliUploadApiFactory();
        using var httpClient = fixture.CreateClient();
        fixture.WriteConfig("https://config.invalid/", fixture.BootstrapToken);
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut,
                                      standardError,
                                      fixture.ConfigFilePath,
                                      new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_UPLOAD_TOKEN"] = fixture.BootstrapToken
                                      },
                                      httpClient);
        var filePath = fixture.CreateInputFile("independent-config.bin", 64);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--server-url", httpClient.BaseAddress!.ToString()], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        Guid.Parse(standardOut.ToString().Trim()).Should().NotBe(Guid.Empty);
        standardError.ToString().Should().Contain("Uploaded file 1 of 1.");
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
    public async Task InvokeAsync_ShouldStoreEncryptedBlobAndNonSecretMetadataOnly()
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
        var filePath = fixture.CreateInputFile(Path.Combine("nested", "secret-notes.bin"), 128);

        var exitCode = await CliApplication.InvokeAsync(["upload", filePath, "--output-secret"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().HaveCount(2);
        var shareSecret = stdoutLines[1].Split(':', 2)[1];
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
        var fileId = Guid.Parse(standardOut.ToString().Trim());
        fixture.GetStoredUploads().Should().ContainSingle(record => record.FileId == fileId && record.OriginalFileName == "--data.bin");
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

    [Test]
    public async Task InvokeAsync_ShouldWriteOnlyFileIdsToStdoutInArgumentOrder_ForSuccessfulMultiFileUploads()
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
        var stdoutLines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        stdoutLines.Should().HaveCount(filePaths.Length);
        standardOut.ToString().Should().NotContain("Uploaded file").And.NotContain("secret:");
        var uploadedFilesById = fixture.GetStoredUploads().ToDictionary(static record => record.FileId);
        stdoutLines.Select(Guid.Parse)
                   .Select(fileId => uploadedFilesById[fileId].OriginalFileName)
                   .Should()
                   .Equal(filePaths.Select(Path.GetFileName));
        standardError.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                     .Should()
                     .Equal("Uploaded file 1 of 3.", "Uploaded file 2 of 3.", "Uploaded file 3 of 3.");
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
                   .And.Contain("--help");
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
                   .And.Contain("--output-secret");
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         String? configPath = null,
                                                         IReadOnlyDictionary<String, String?>? environmentValues = null,
                                                         HttpClient? httpClient = null)
    {
        var resolvedHttpClient = httpClient ?? new HttpClient(new NeverCalledHandler());
        return new(new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(environmentValues ?? new Dictionary<String, String?>())),
                   resolvedHttpClient,
                   standardOut,
                   standardError);
    }

    private static CliApplicationServices CreateServices(
        TextWriter standardOut,
        TextWriter standardError,
        String? configPath = null,
        IReadOnlyDictionary<String, String?>? environmentValues = null,
        HttpClient? httpClient = null) =>
        new(new(new StubConfigPathResolver(configPath), new StubEnvironmentReader(environmentValues ?? new Dictionary<String, String?>())),
            httpClient ?? new HttpClient(new NeverCalledHandler()),
            standardOut,
            standardError);

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
