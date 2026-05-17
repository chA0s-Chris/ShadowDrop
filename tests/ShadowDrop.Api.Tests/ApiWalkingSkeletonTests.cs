// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

[TestFixture]
[NonParallelizable]
public sealed class ApiWalkingSkeletonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task HealthEndpoint_ShouldBeAvailableWithoutAuthentication()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var unauthenticatedResponse = await client.GetAsync("/api/admin/management/ping");
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var authenticatedResponse = await client.GetAsync("/api/admin/management/ping");

        unauthenticatedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Startup_ShouldCreateConfiguredMetadataAndStorageDirectories()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        _ = await client.GetAsync("/health");

        Directory.Exists(Path.GetDirectoryName(fixture.MetadataDatabasePath)!).Should().BeTrue();
        Directory.Exists(fixture.LocalStorageRoot).Should().BeTrue();
    }

    [Test]
    public async Task BootstrapToken_ShouldBeStoredAsProtectedHash()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        _ = await client.GetAsync("/api/admin/management/ping");

        using var database = new LiteDatabase(fixture.MetadataDatabasePath);
        var credentials = database.GetCollection<AdminTokenCredentialDocument>("admin_tokens");
        var credential = credentials.FindById(1);

        credential.Should().NotBeNull();
        credential.TokenHashBase64.Should().NotBe(fixture.BootstrapToken);
        credential.SaltBase64.Should().NotBeNullOrWhiteSpace();
        credential.Iterations.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldNotBeMapped_WhenAdminOperationsExposureIsDisabled()
    {
        await using var fixture = new TestApiFactory(false);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        var response = await client.GetAsync("/api/admin/management/ping");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldReturn401_ForWrongBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "definitely-not-the-right-token");

        var response = await client.GetAsync("/api/admin/management/ping");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UploadRoute_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        using var validRequestContent = CreateValidUploadContent();

        var noAuthResponse = await client.PostAsync("/api/admin/uploads", CreateValidUploadContent());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var wrongTokenResponse = await client.PostAsync("/api/admin/uploads", CreateValidUploadContent());
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var validTokenResponse = await client.PostAsync("/api/admin/uploads", validRequestContent);

        noAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        validTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task UploadRoute_ShouldPersistEncryptedBlobAndMetadata()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent();

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploadResult = await response.Content.ReadFromJsonAsync<UploadResult>();
        uploadResult.Should().NotBeNull();
        using var uploadResponseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        uploadResponseDocument.RootElement.TryGetProperty("kdfSaltBase64", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("plaintextSha256", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("blobKey", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("originalFileName", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("contentType", out _).Should().BeFalse();

        var repository = fixture.Services.GetRequiredService<IUploadedFileMetadataRepository>();
        var storedRecord = await repository.GetAsync(uploadResult!.FileId, CancellationToken.None);
        storedRecord.Should().NotBeNull();
        storedRecord.OriginalFileName.Should().Be("cipher.bin");
        storedRecord.ContentType.Should().Be("application/octet-stream");

        var blobPath = Path.Combine(fixture.LocalStorageRoot, storedRecord.BlobKey);
        File.Exists(blobPath).Should().BeTrue();
        var blobContent = await File.ReadAllBytesAsync(blobPath);
        blobContent.Should().Equal(CreateCiphertext());

        var metadataResponse = await client.GetAsync($"/api/admin/uploads/{uploadResult.FileId}");
        metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(blobPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(fixture.MetadataDatabasePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_AndNotPersist_WhenEncryptedLengthIsInconsistent()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(CreateCiphertext().LongLength + 1));

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid upload request.");
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_ForNonMultipartRequests()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = JsonContent.Create(new
        {
            anything = "nope"
        });

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_WhenMetadataPartUsesWrongContentType()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(metadataContentType: "text/plain");

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_WhenChunkMetadataIsInconsistent()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload() with
        {
            ChunkCount = 3
        });

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_WhenMultipartEnvelopeContainsUnexpectedSections()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(includeUnexpectedSection: true);

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task UploadRoute_ShouldReturn429_WhenRateLimitIsExceeded()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var successfulRequest = CreateValidUploadContent();
            var successfulResponse = await client.PostAsync("/api/admin/uploads", successfulRequest);
            successfulResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var throttledRequest = CreateValidUploadContent();
        var throttledResponse = await client.PostAsync("/api/admin/uploads", throttledRequest);

        throttledResponse.StatusCode.Should().Be((HttpStatusCode)429);
        (await throttledResponse.Content.ReadAsStringAsync()).Should().Be("""{"error":"Too many requests."}""");
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn200_WhenPublicDownloadsAreEnabled()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn404_WhenPublicDownloadsAreDisabled()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: false);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public void Startup_ShouldFail_WhenBootstrapAdminTokenIsMissingOnFirstBoot()
    {
        using var fixture = new TestApiFactory(withBootstrapToken: false);

        Action act = () => fixture.CreateClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public async Task Config_RelativePaths_ShouldBeResolvedToAbsolutePathsUnderContentRoot()
    {
        await using var fixture = new TestApiFactory(useRelativePaths: true);
        using var client = fixture.CreateClient();

        _ = await client.GetAsync("/health");

        var options = fixture.Services.GetRequiredService<ShadowDropOptions>();
        var contentRoot = fixture.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;

        Path.IsPathRooted(options.Metadata.LiteDbPath).Should().BeTrue("a relative metadata path must be resolved to an absolute path");
        Path.IsPathRooted(options.Storage.LocalRoot).Should().BeTrue("a relative storage path must be resolved to an absolute path");
        options.Metadata.LiteDbPath.Should().StartWith(contentRoot, "the resolved metadata path must be anchored to the content root");
        options.Storage.LocalRoot.Should().StartWith(contentRoot, "the resolved storage path must be anchored to the content root");
        Directory.Exists(Path.GetDirectoryName(options.Metadata.LiteDbPath)!).Should().BeTrue();
        Directory.Exists(options.Storage.LocalRoot).Should().BeTrue();
    }

    [Test]
    public async Task Startup_ShouldSucceed_WhenAdminOperationsAreDisabled_EvenWithoutBootstrapToken()
    {
        await using var fixture = new TestApiFactory(false, withBootstrapToken: false);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
                                        "when admin operations are disabled, AdminTokenService is never initialized so no bootstrap token is required");
    }

    [Test]
    public async Task Startup_BootstrapFailure_ShouldLeaveEnvironmentClean_ForSubsequentStartup()
    {
        var tokenBefore = Environment.GetEnvironmentVariable(TestApiFactory.BootstrapTokenEnvironmentVariable);

        var failedFixture = new TestApiFactory(withBootstrapToken: false);
        Action act = () => failedFixture.CreateClient();
        act.Should().Throw<InvalidOperationException>();
        await failedFixture.DisposeAsync();

        Environment.GetEnvironmentVariable(TestApiFactory.BootstrapTokenEnvironmentVariable).Should().Be(tokenBefore,
                                                                                                         "disposal of a failed factory must restore the bootstrap token environment variable to its pre-test state");

        await using var healthyFixture = new TestApiFactory();
        using var client = healthyFixture.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
                                        "disposal of a failed factory must restore environment variables so subsequent factories can start");
    }

    private sealed class AdminTokenCredentialDocument
    {
        public Int32 Id { get; set; }

        public Int32 Iterations { get; set; }

        public String SaltBase64 { get; set; } = String.Empty;

        public String TokenHashBase64 { get; set; } = String.Empty;
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        public const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
        private const String AdminOperationsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnableAdminOperations";
        private const String MetadataPathEnvironmentVariable = "ShadowDrop__Metadata__LiteDbPath";
        private const String PublicDownloadsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnablePublicDownloads";
        private const String StorageRootEnvironmentVariable = "ShadowDrop__Storage__LocalRoot";
        private readonly String? _previousAdminOperationsExposure;
        private readonly String? _previousBootstrapToken;
        private readonly String? _previousMetadataPath;
        private readonly String? _previousPublicDownloadsExposure;
        private readonly String? _previousStorageRoot;
        private readonly String? _relativePathPrefix;
        private readonly String _temporaryRootDirectory;
        private readonly Boolean _useRelativePaths;
        private String? _resolvedRelativeRoot;

        public TestApiFactory(Boolean enableAdminOperations = true, Boolean enablePublicDownloads = true, Boolean withBootstrapToken = true,
                              Boolean useRelativePaths = false)
        {
            _useRelativePaths = useRelativePaths;
            BootstrapToken = "test-bootstrap-token";
            EnableAdminOperations = enableAdminOperations;

            if (useRelativePaths)
            {
                _relativePathPrefix = $"reltest-{Guid.NewGuid():N}";
                _temporaryRootDirectory = String.Empty;
                MetadataDatabasePath = Path.Combine("data", _relativePathPrefix, "metadata", "shadowdrop.db");
                LocalStorageRoot = Path.Combine(_relativePathPrefix, "storage");
            }
            else
            {
                _temporaryRootDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts", $"shadowdrop-api-tests-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_temporaryRootDirectory);
                MetadataDatabasePath = Path.Combine(_temporaryRootDirectory, "metadata", "shadowdrop.db");
                LocalStorageRoot = Path.Combine(_temporaryRootDirectory, "storage");
            }

            _previousBootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable);
            _previousMetadataPath = Environment.GetEnvironmentVariable(MetadataPathEnvironmentVariable);
            _previousStorageRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            _previousPublicDownloadsExposure = Environment.GetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable);
            _previousAdminOperationsExposure = Environment.GetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable);

            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, withBootstrapToken ? BootstrapToken : null);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, MetadataDatabasePath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, LocalStorageRoot);
            Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, enablePublicDownloads ? "true" : "false");
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, enableAdminOperations ? "true" : "false");
        }

        public String BootstrapToken { get; }

        public Boolean EnableAdminOperations { get; }

        public String LocalStorageRoot { get; }

        public String MetadataDatabasePath { get; }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                if (_useRelativePaths && _relativePathPrefix is not null)
                {
                    try
                    {
                        var contentRoot = Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
                        _resolvedRelativeRoot = Path.Combine(contentRoot, _relativePathPrefix);
                    }
                    catch
                    {
                        // best-effort: Services may be inaccessible if startup failed
                    }
                }

                Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, _previousBootstrapToken);
                Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, _previousMetadataPath);
                Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, _previousStorageRoot);
                Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, _previousPublicDownloadsExposure);
                Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, _previousAdminOperationsExposure);
            }

            base.Dispose(disposing);

            if (!String.IsNullOrEmpty(_temporaryRootDirectory) && Directory.Exists(_temporaryRootDirectory))
            {
                Directory.Delete(_temporaryRootDirectory, true);
            }

            if (_resolvedRelativeRoot is not null && Directory.Exists(_resolvedRelativeRoot))
            {
                Directory.Delete(_resolvedRelativeRoot, true);
            }
        }
    }

    private static Byte[] CreateCiphertext() => Enumerable.Range(0, 160).Select(value => (Byte)value).ToArray();

    private static UploadMetadataPayload CreateValidMetadataPayload(Int64? encryptedLength = null)
    {
        var ciphertext = CreateCiphertext();
        return new(
            "cipher.bin",
            128,
            encryptedLength ?? ciphertext.LongLength,
            "application/octet-stream",
            FormatConstants.EncryptionFormatVersion,
            FormatConstants.Aes256GcmAlgorithmId,
            64,
            2,
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            new('a', 64));
    }

    private static MultipartFormDataContent CreateValidUploadContent(UploadMetadataPayload? metadata = null,
                                                                     String metadataContentType = "application/json",
                                                                     String contentContentType = "application/octet-stream",
                                                                     Boolean includeUnexpectedSection = false)
    {
        metadata ??= CreateValidMetadataPayload();

        var content = new MultipartFormDataContent();
        var metadataContent = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
        metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse(metadataContentType);
        content.Add(metadataContent, "metadata");

        var fileContent = new ByteArrayContent(CreateCiphertext());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentContentType);
        content.Add(fileContent, "content", "cipher.bin");

        if (includeUnexpectedSection)
        {
            content.Add(new StringContent("surprise", Encoding.UTF8), "unexpected");
        }

        return content;
    }

    private sealed record UploadMetadataPayload(
        String OriginalFileName,
        Int64 PlaintextLength,
        Int64 EncryptedLength,
        String ContentType,
        String EncryptionFormatVersion,
        String AlgorithmId,
        Int32 ChunkSize,
        Int64 ChunkCount,
        String KdfSalt,
        String? PlaintextSha256);
}
