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
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    public async Task ShareRoute_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var request = CreateValidShareRequest(fileId, false);
        client.DefaultRequestHeaders.Authorization = null;

        var noAuthResponse = await client.PostAsJsonAsync("/api/admin/shares", request);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var wrongTokenResponse = await client.PostAsJsonAsync("/api/admin/shares", request);
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var validTokenResponse = await client.PostAsJsonAsync("/api/admin/shares", request);

        noAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        validTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task ShareRoute_ShouldPersistHashedTokensAndNeverReturnHashes()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var request = CreateValidShareRequest(fileId, true);

        var response = await client.PostAsJsonAsync("/api/admin/shares", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var responsePayload = await response.Content.ReadAsStringAsync();
        var createShareResult = JsonSerializer.Deserialize<CreateShareResult>(responsePayload, JsonOptions);
        createShareResult.Should().NotBeNull();
        createShareResult!.ShareToken.Should().NotBeNullOrWhiteSpace();
        createShareResult.DownloadBearerToken.Should().NotBeNullOrWhiteSpace();

        using var responseDocument = JsonDocument.Parse(responsePayload);
        responseDocument.RootElement.TryGetProperty("shareTokenHashBase64", out _).Should().BeFalse();
        responseDocument.RootElement.TryGetProperty("downloadBearerTokenHashBase64", out _).Should().BeFalse();

        var repository = fixture.Services.GetRequiredService<IShareMetadataRepository>();
        var storedShare = await repository.GetAsync(createShareResult.ShareId, CancellationToken.None);
        storedShare.Should().NotBeNull();
        storedShare!.ShareTokenHashBase64.Should().NotBe(createShareResult.ShareToken);
        storedShare.DownloadBearerToken.Should().NotBeNull();
        storedShare.DownloadBearerToken!.TokenHashBase64.Should().NotBe(createShareResult.DownloadBearerToken);
        storedShare.ExpiresAtUtc.Should().BeCloseTo(request.ExpiresAtUtc, TimeSpan.FromSeconds(1));
        storedShare.RevokedAtUtc.Should().BeNull();
        storedShare.CleanupState.Should().Be(ShareCleanupState.Pending);
        storedShare.DirectHttpEnabled.Should().BeFalse();
        storedShare.Files.Should().ContainSingle();
        storedShare.Files[0].FileId.Should().Be(fileId);
        storedShare.Files[0].DisplayName.Should().Be("renamed.bin");
    }

    [Test]
    public async Task ShareRoute_ShouldReturn400_ForInvalidRequests()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        var duplicateFileResponse = await client.PostAsJsonAsync("/api/admin/shares", new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                                                     [new(fileId), new(fileId)],
                                                                     GenerateDownloadBearerToken: false));
        var missingFileResponse =
            await client.PostAsJsonAsync("/api/admin/shares", CreateValidShareRequest(Guid.NewGuid(), false));
        var invalidModeResponse = await client.PostAsJsonAsync("/api/admin/shares", new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                                                   [new(fileId)],
                                                                   true,
                                                                   true,
                                                                   DateTimeOffset.Parse("2026-05-30T00:00:00Z")));
        var missingTokenSetupResponse = await client.PostAsJsonAsync("/api/admin/shares", new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                                                         [new(fileId)]));

        duplicateFileResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingFileResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidModeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingTokenSetupResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidModeResponse.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid share request."}""");
    }

    [Test]
    public async Task UploadRoute_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        var validFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        using var validRequestContent = CreateValidUploadContent(CreateValidMetadataPayload(validFileId));

        var noAuthFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        var noAuthResponse = await client.PostAsync("/api/admin/uploads", CreateValidUploadContent(CreateValidMetadataPayload(noAuthFileId)));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var wrongTokenFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        var wrongTokenResponse = await client.PostAsync("/api/admin/uploads", CreateValidUploadContent(CreateValidMetadataPayload(wrongTokenFileId)));
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var validTokenResponse = await client.PostAsync("/api/admin/uploads", validRequestContent);

        noAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        validTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task UploadReservationRoute_ShouldRequireValidAdminBearerToken_AndReturnServerIssuedFileId()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var noAuthResponse = await client.PostAsync("/api/admin/uploads/reservations", null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var wrongTokenResponse = await client.PostAsync("/api/admin/uploads/reservations", null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var validTokenResponse = await client.PostAsync("/api/admin/uploads/reservations", null);

        noAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        validTokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var reservation = JsonSerializer.Deserialize<UploadReservationResult>(await validTokenResponse.Content.ReadAsStringAsync(), JsonOptions);
        reservation.Should().NotBeNull();
        reservation!.FileId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task UploadRoute_ShouldPersistEncryptedBlobAndMetadata()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var reservedFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId));

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var responsePayload = await response.Content.ReadAsStringAsync();
        var uploadResult = JsonSerializer.Deserialize<UploadResult>(responsePayload, JsonOptions);
        uploadResult.Should().NotBeNull();
        uploadResult!.FileId.Should().Be(reservedFileId);
        using var uploadResponseDocument = JsonDocument.Parse(responsePayload);
        uploadResponseDocument.RootElement.TryGetProperty("kdfSaltBase64", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("plaintextSha256", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("blobKey", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("originalFileName", out _).Should().BeFalse();
        uploadResponseDocument.RootElement.TryGetProperty("contentType", out _).Should().BeFalse();

        var repository = fixture.Services.GetRequiredService<IUploadedFileMetadataRepository>();
        var storedRecord = await repository.GetAsync(uploadResult.FileId, CancellationToken.None);
        storedRecord.Should().NotBeNull();
        storedRecord!.FileId.Should().Be(reservedFileId);
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
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(encryptedLength: CreateCiphertext().LongLength + 1));

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
    public async Task UploadRoute_ShouldReturn400_AndNotPersist_WhenFileIdWasNotReserved()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(Guid.NewGuid()));

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_AndNotPersist_WhenFileIdReservationExpired()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var reservedFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        ExpireReservation(fixture.MetadataDatabasePath, reservedFileId);
        using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId));

        var response = await client.PostAsync("/api/admin/uploads", requestContent);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        using var verificationDatabase = new LiteDatabase(fixture.MetadataDatabasePath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(reservedFileId)).Should().BeNull();
    }

    [Test]
    public async Task UploadRoute_ShouldReturn400_AndPreserveExistingBlob_WhenCompletedFileIdIsReused()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var reservedFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        using var initialRequest = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId));

        var initialResponse = await client.PostAsync("/api/admin/uploads", initialRequest);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var repository = fixture.Services.GetRequiredService<IUploadedFileMetadataRepository>();
        var storedRecord = await repository.GetAsync(reservedFileId, CancellationToken.None);
        storedRecord.Should().NotBeNull();
        var blobPath = Path.Combine(fixture.LocalStorageRoot, storedRecord!.BlobKey);
        var originalBlob = await File.ReadAllBytesAsync(blobPath);

        using var duplicateRequest = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId),
                                                              new Byte[]
                                                              {
                                                                  1,
                                                                  2,
                                                                  3,
                                                                  4,
                                                                  5,
                                                                  6,
                                                                  7,
                                                                  8
                                                              });

        var duplicateResponse = await client.PostAsync("/api/admin/uploads", duplicateRequest);

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicateResponse.Content.ReadAsStringAsync()).Should().Be("""{"error":"Invalid upload request."}""");
        (await File.ReadAllBytesAsync(blobPath)).Should().Equal(originalBlob);
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
            var reservedFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
            using var successfulRequest = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId));
            var successfulResponse = await client.PostAsync("/api/admin/uploads", successfulRequest);
            successfulResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var throttledFileId = await ReserveFileIdAsync(client, fixture.BootstrapToken);
        using var throttledRequest = CreateValidUploadContent(CreateValidMetadataPayload(throttledFileId));
        var throttledResponse = await client.PostAsync("/api/admin/uploads", throttledRequest);

        throttledResponse.StatusCode.Should().Be((HttpStatusCode)429);
        (await throttledResponse.Content.ReadAsStringAsync()).Should().Be("""{"error":"Too many requests."}""");
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldStreamEncryptedFile_WithHeaders_ForCliDecryptMode()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client, fixture.BootstrapToken, CreateValidShareRequest(fileId, false));

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{fileId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(CreateCiphertext().LongLength);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("renamed.bin");
        response.Headers.GetValues("X-ShadowDrop-Download-Mode").Should().ContainSingle("cli-decrypt");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(CreateCiphertext());
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn401_WhenShareIsExpired()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(fileId,
                                                                   false,
                                                                   expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{fileId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn403_WhenRequiredDownloadBearerTokenIsMissing()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client, fixture.BootstrapToken, CreateValidShareRequest(fileId, true));
        client.DefaultRequestHeaders.Authorization.Should().BeNull();

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{fileId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldIgnoreBearerTokenQueryParameter()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client, fixture.BootstrapToken, CreateValidShareRequest(fileId, true));
        client.DefaultRequestHeaders.Authorization.Should().BeNull();

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{fileId}?access_token={share.DownloadBearerToken}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldAcceptRequiredDownloadBearerToken_FromAuthorizationHeader()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client, fixture.BootstrapToken, CreateValidShareRequest(fileId, true));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/d/{share.ShareToken}/files/{fileId}");
        request.Headers.Authorization = new("Bearer", share.DownloadBearerToken);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-ShadowDrop-Download-Mode").Should().ContainSingle("cli-decrypt");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(CreateCiphertext());
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldAcceptDirectHttpKeyMaterial_FromHeader()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));
        client.DefaultRequestHeaders.Add(DownloadKeyConstants.HeaderName, directHttpFixture.KeyMaterialBase64);

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{directHttpFixture.FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(directHttpFixture.Plaintext.LongLength);
        response.Headers.GetValues("X-ShadowDrop-Download-Mode").Should().ContainSingle("direct-http");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(directHttpFixture.Plaintext);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldDecryptDirectHttpFile_ThroughRealUploadAndShareFlow()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));
        client.DefaultRequestHeaders.Add(DownloadKeyConstants.HeaderName, directHttpFixture.KeyMaterialBase64);

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{directHttpFixture.FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(directHttpFixture.Plaintext.LongLength);
        response.Headers.GetValues("X-ShadowDrop-Download-Mode").Should().ContainSingle("direct-http");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(directHttpFixture.Plaintext);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn400_WhenDirectHttpKeyMaterialIsProvidedInHeaderAndQuery()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));
        client.DefaultRequestHeaders.Add(DownloadKeyConstants.HeaderName, directHttpFixture.KeyMaterialBase64);

        var response = await client.GetAsync(
            $"/d/{share.ShareToken}/files/{directHttpFixture.FileId}?{DownloadKeyConstants.QueryParameterName}={directHttpFixture.KeyMaterialBase64}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn400_WhenDirectHttpKeyMaterialIsMissingFromHeaderAndQuery()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{directHttpFixture.FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldAcceptDirectHttpKeyMaterial_FromQueryParameter()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));

        var response = await client.GetAsync(
            $"/d/{share.ShareToken}/files/{directHttpFixture.FileId}?{DownloadKeyConstants.QueryParameterName}={WebUtility.UrlEncode(directHttpFixture.KeyMaterialBase64)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(directHttpFixture.Plaintext.LongLength);
        response.Headers.GetValues("X-ShadowDrop-Download-Mode").Should().ContainSingle("direct-http");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(directHttpFixture.Plaintext);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn400_WhenDirectHttpKeyMaterialIsWrong()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));
        client.DefaultRequestHeaders.Add(DownloadKeyConstants.HeaderName, directHttpFixture.WrongKeyMaterialBase64);

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{directHttpFixture.FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn404_WhenCliDecryptBlobIsMissing()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client, fixture.BootstrapToken, CreateValidShareRequest(fileId, false));
        DeleteUploadedBlob(fixture, fileId);

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{fileId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn404_WhenDirectHttpBlobIsMissing()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var directHttpFixture = await UploadDirectHttpFileAsync(client, fixture.BootstrapToken);
        var share = await CreateShareAsync(client,
                                           fixture.BootstrapToken,
                                           CreateValidShareRequest(directHttpFixture.FileId, false, true));
        DeleteUploadedBlob(fixture, directHttpFixture.FileId);
        client.DefaultRequestHeaders.Add(DownloadKeyConstants.HeaderName, directHttpFixture.KeyMaterialBase64);

        var response = await client.GetAsync($"/d/{share.ShareToken}/files/{directHttpFixture.FileId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn401_WhenShareTokenIsInvalid()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: true);
        using var client = fixture.CreateClient();
        var fileId = await UploadValidFileAsync(client, fixture.BootstrapToken);

        var response = await client.GetAsync($"/d/{Guid.NewGuid():N}/files/{fileId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn404_WhenPublicDownloadsAreDisabled()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: false);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync($"/d/{Guid.NewGuid():N}/files/{Guid.NewGuid()}");

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


    private static CreateShareRequest CreateValidShareRequest(Guid fileId,
                                                              Boolean generateDownloadBearerToken,
                                                              Boolean directHttpEnabled = false,
                                                              DateTimeOffset? expiresAtUtc = null) =>
        new(expiresAtUtc ?? DateTimeOffset.UtcNow.AddDays(1),
            [new(fileId, "renamed.bin")],
            directHttpEnabled,
            generateDownloadBearerToken,
            generateDownloadBearerToken ? DateTimeOffset.UtcNow.AddHours(12) : null);

    private static async Task<CreateShareResult> CreateShareAsync(HttpClient client, String bootstrapToken, CreateShareRequest request)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;

        try
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", bootstrapToken);
            var response = await client.PostAsJsonAsync("/api/admin/shares", request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return JsonSerializer.Deserialize<CreateShareResult>(await response.Content.ReadAsStringAsync(), JsonOptions)!;
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }
    }

    private static async Task<Guid> UploadValidFileAsync(HttpClient client, String bootstrapToken)
    {
        var reservedFileId = await ReserveFileIdAsync(client, bootstrapToken);
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;

        try
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", bootstrapToken);
            using var requestContent = CreateValidUploadContent(CreateValidMetadataPayload(reservedFileId));
            var response = await client.PostAsync("/api/admin/uploads", requestContent);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var payload = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<UploadResult>(payload, JsonOptions)!.FileId;
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }
    }

    private static async Task<DirectHttpDownloadFixture> UploadDirectHttpFileAsync(HttpClient client, String bootstrapToken)
    {
        var reservedFileId = await ReserveFileIdAsync(client, bootstrapToken);
        var payload = CreateDirectHttpPayload(reservedFileId);
        var uploadMetadata = CreateValidMetadataPayload(reservedFileId,
                                                        "cipher.bin",
                                                        payload.Plaintext.LongLength,
                                                        payload.Ciphertext.LongLength,
                                                        payload.ChunkSize,
                                                        payload.ChunkCount,
                                                        payload.KdfSaltBase64,
                                                        payload.PlaintextSha256);
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;

        try
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", bootstrapToken);
            using var requestContent = CreateValidUploadContent(uploadMetadata, payload.Ciphertext);
            var response = await client.PostAsync("/api/admin/uploads", requestContent);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var uploadResult = JsonSerializer.Deserialize<UploadResult>(await response.Content.ReadAsStringAsync(), JsonOptions);
            uploadResult.Should().NotBeNull();
            uploadResult!.FileId.Should().Be(reservedFileId);
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }

        return new(String.Empty, reservedFileId, payload.KeyMaterialBase64, payload.WrongKeyMaterialBase64, payload.Plaintext);
    }

    private static async Task<Guid> ReserveFileIdAsync(HttpClient client, String bootstrapToken)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;

        try
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", bootstrapToken);
            var response = await client.PostAsync("/api/admin/uploads/reservations", null);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var reservation = JsonSerializer.Deserialize<UploadReservationResult>(await response.Content.ReadAsStringAsync(), JsonOptions);
            reservation.Should().NotBeNull();
            reservation!.FileId.Should().NotBe(Guid.Empty);
            return reservation.FileId;
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = previousAuthorization;
        }
    }

    private static void DeleteUploadedBlob(TestApiFactory fixture, Guid fileId)
    {
        var repository = fixture.Services.GetRequiredService<IUploadedFileMetadataRepository>();
        var uploadedFile = repository.GetAsync(fileId, CancellationToken.None).GetAwaiter().GetResult();
        uploadedFile.Should().NotBeNull();

        var blobPath = Path.Combine(fixture.LocalStorageRoot, uploadedFile!.BlobKey);
        File.Exists(blobPath).Should().BeTrue();
        File.Delete(blobPath);
        File.Exists(blobPath).Should().BeFalse();
    }

    private static void ExpireReservation(String databasePath, Guid fileId)
    {
        using var database = new LiteDatabase(databasePath);
        var collection = database.GetCollection("uploaded_files");
        var document = collection.FindById(fileId);
        ((Object?)document).Should().NotBeNull();
        document!["ReservedAtUnixTimeMilliseconds"] = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        collection.Update(document);
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

    private static DirectHttpPayload CreateDirectHttpPayload(Guid fileId)
    {
        var plaintext = Enumerable.Range(0, 128).Select(value => (Byte)(255 - value)).ToArray();
        var keyMaterial = Enumerable.Range(1, 32).Select(value => (Byte)value).ToArray();
        var wrongKeyMaterial = Enumerable.Range(65, 32).Select(value => (Byte)value).ToArray();
        var kdfSalt = Enumerable.Range(129, 32).Select(value => (Byte)value).ToArray();
        using var shareSecret = ShareSecret.FromBytes(keyMaterial);
        var context = new FileEncryptionContext(fileId, kdfSalt);
        using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, context);
        using var ciphertext = new MemoryStream();
        const Int32 chunkSize = 64;
        var chunkCount = plaintext.LongLength / chunkSize;

        for (var chunkIndex = 0L; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunkPlaintext = plaintext.Skip((Int32)(chunkIndex * chunkSize)).Take(chunkSize).ToArray();
            var metadata = new ChunkMetadata(CryptoVersion.V1,
                                             CryptoAlgorithm.Aes256Gcm,
                                             fileId,
                                             chunkSize,
                                             chunkIndex,
                                             chunkPlaintext.Length);
            var encryptedChunk = ChunkEncryptionService.EncryptChunk(chunkPlaintext, contentKey, metadata);
            ciphertext.Write(encryptedChunk.Ciphertext);
        }

        return new(ciphertext.ToArray(),
                   plaintext,
                   chunkSize,
                   chunkCount,
                   Convert.ToBase64String(kdfSalt),
                   Convert.ToBase64String(keyMaterial),
                   Convert.ToBase64String(wrongKeyMaterial),
                   Convert.ToHexStringLower(SHA256.HashData(plaintext)));
    }

    private static UploadMetadataPayload CreateValidMetadataPayload(Guid? fileId = null,
                                                                    String originalFileName = "cipher.bin",
                                                                    Int64 plaintextLength = 128,
                                                                    Int64? encryptedLength = null,
                                                                    Int32 chunkSize = 64,
                                                                    Int64 chunkCount = 2,
                                                                    String? kdfSalt = null,
                                                                    String? plaintextSha256 = null)
    {
        var ciphertext = CreateCiphertext();
        return new(
            fileId ?? Guid.NewGuid(),
            originalFileName,
            plaintextLength,
            encryptedLength ?? ciphertext.LongLength,
            "application/octet-stream",
            FormatConstants.EncryptionFormatVersion,
            FormatConstants.Aes256GcmAlgorithmId,
            chunkSize,
            chunkCount,
            kdfSalt ?? Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            plaintextSha256 ?? new('a', 64));
    }

    private static MultipartFormDataContent CreateValidUploadContent(UploadMetadataPayload? metadata = null,
                                                                     Byte[]? ciphertext = null,
                                                                     String metadataContentType = "application/json",
                                                                     String contentContentType = "application/octet-stream",
                                                                     Boolean includeUnexpectedSection = false)
    {
        metadata ??= CreateValidMetadataPayload();

        var content = new MultipartFormDataContent();
        var metadataContent = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
        metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse(metadataContentType);
        content.Add(metadataContent, "metadata");

        var fileContent = new ByteArrayContent(ciphertext ?? CreateCiphertext());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentContentType);
        content.Add(fileContent, "content", "cipher.bin");

        if (includeUnexpectedSection)
        {
            content.Add(new StringContent("surprise", Encoding.UTF8), "unexpected");
        }

        return content;
    }

    private sealed record UploadMetadataPayload(
        Guid FileId,
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

    private sealed record DirectHttpDownloadFixture(
        String ShareToken,
        Guid FileId,
        String KeyMaterialBase64,
        String WrongKeyMaterialBase64,
        Byte[] Plaintext);

    private sealed record DirectHttpPayload(
        Byte[] Ciphertext,
        Byte[] Plaintext,
        Int32 ChunkSize,
        Int64 ChunkCount,
        String KdfSaltBase64,
        String KeyMaterialBase64,
        String WrongKeyMaterialBase64,
        String PlaintextSha256);
}
