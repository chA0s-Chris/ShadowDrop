// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using LiteDB;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

[NonParallelizable]
public sealed class ScopedUploadEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task RevokingCredential_ShouldPreventNewOperations_WithoutRevokingExistingShare()
    {
        await using var fixture = new ScopedApiFactory();
        using var adminClient = fixture.CreateClientWithToken(fixture.BootstrapToken);
        var credential = await CreateCredentialAsync(adminClient, "revoked-after-share");
        using var scopedClient = fixture.CreateClientWithToken(credential.Token);
        var fileId = await ReserveAsync(scopedClient);
        await UploadAsync(scopedClient, fileId);
        var shareResponse = await scopedClient.PostAsJsonAsync("/api/shares/", CreateShareRequest(fileId));
        var share = await shareResponse.Content.ReadFromJsonAsync<CreateShareResult>(JsonOptions);

        (await adminClient.PostAsync($"/api/admin/upload-credentials/{credential.CredentialId}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await scopedClient.GetAsync("/api/uploads/capabilities")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var publicClient = fixture.CreateClient();
        (await publicClient.GetAsync($"/d/{share!.ShareToken}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var storedShare = await fixture.Services.GetRequiredService<IShareMetadataRepository>()
                                       .GetAsync(share.ShareId, CancellationToken.None);
        storedShare!.RevokedAtUtc.Should().BeNull();
    }

    [Test]
    public async Task ScopedConstraints_ShouldLimitFileAndAggregateEncryptedBytes()
    {
        await using var fixture = new ScopedApiFactory();
        using var adminClient = fixture.CreateClientWithToken(fixture.BootstrapToken);
        var constrained = await CreateCredentialAsync(adminClient, "constrained", 160, 300);
        using var client = fixture.CreateClientWithToken(constrained.Token);

        var capabilities = await client.GetFromJsonAsync<UploadCapabilitiesResult>("/api/uploads/capabilities", JsonOptions);
        capabilities!.MaxFilePayloadBytes.Should().Be(160);
        capabilities.MaxEncryptedShareBytes.Should().Be(300);

        var firstFile = await ReserveAsync(client);
        await UploadAsync(client, firstFile);
        var secondFile = await ReserveAsync(client);
        await UploadAsync(client, secondFile);
        var aggregateResponse = await client.PostAsJsonAsync(
            "/api/shares/",
            new CreateShareRequest(DateTimeOffset.UtcNow.AddDays(1),
                                   [new(firstFile), new(secondFile)],
                                   GenerateDownloadBearerToken: false));
        aggregateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tooSmall = await CreateCredentialAsync(adminClient, "too-small", 159, null);
        using var tooSmallClient = fixture.CreateClientWithToken(tooSmall.Token);
        var reservation = await ReserveAsync(tooSmallClient);
        using var upload = CreateUploadContent(reservation);
        var oversizedResponse = await tooSmallClient.PostAsync("/api/uploads", upload);
        oversizedResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Directory.EnumerateFiles(fixture.LocalStorageRoot, "*", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Test]
    public async Task ScopedCredential_ShouldReachOnlyScopedRoutes_AndUseGenericAuthenticationFailures()
    {
        await using var fixture = new ScopedApiFactory();
        using var adminClient = fixture.CreateClientWithToken(fixture.BootstrapToken);
        var active = await CreateCredentialAsync(adminClient, "active");
        var expired = await CreateCredentialAsync(adminClient, "expired");
        ExpireCredential(fixture.MetadataDatabasePath, expired.CredentialId);
        var revoked = await CreateCredentialAsync(adminClient, "revoked");
        (await adminClient.PostAsync($"/api/admin/upload-credentials/{revoked.CredentialId}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scopedClient = fixture.CreateClientWithToken(active.Token);
        (await scopedClient.GetAsync("/api/uploads/capabilities")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await scopedClient.GetAsync("/api/admin/management/ping")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await scopedClient.PostAsync("/api/admin/shares/cleanup", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await scopedClient.GetAsync("/api/admin/upload-credentials/")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthenticated = await fixture.CreateClient().GetAsync("/api/uploads/capabilities");
        using var malformedClient = fixture.CreateClientWithToken("sdu1.malformed");
        var malformed = await malformedClient.GetAsync("/api/uploads/capabilities");
        using var expiredClient = fixture.CreateClientWithToken(expired.Token);
        var expiredResponse = await expiredClient.GetAsync("/api/uploads/capabilities");
        using var revokedClient = fixture.CreateClientWithToken(revoked.Token);
        var revokedResponse = await revokedClient.GetAsync("/api/uploads/capabilities");

        foreach (var response in new[]
                 {
                     unauthenticated,
                     malformed,
                     expiredResponse,
                     revokedResponse
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        }
    }

    [Test]
    public async Task ScopedWorkflow_ShouldEnforceReservationFileAndShareOwnership()
    {
        await using var fixture = new ScopedApiFactory();
        using var adminClient = fixture.CreateClientWithToken(fixture.BootstrapToken);
        var owner = await CreateCredentialAsync(adminClient, "owner");
        var other = await CreateCredentialAsync(adminClient, "other");
        using var ownerClient = fixture.CreateClientWithToken(owner.Token);
        using var otherClient = fixture.CreateClientWithToken(other.Token);

        var ownerReservation = await ReserveAsync(ownerClient);
        using (var foreignUpload = CreateUploadContent(ownerReservation))
        {
            (await otherClient.PostAsync("/api/uploads", foreignUpload)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        await UploadAsync(ownerClient, ownerReservation);
        var stored = await fixture.Services.GetRequiredService<IUploadedFileMetadataRepository>()
                                  .GetAsync(ownerReservation, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.OwnerCredentialId.Should().Be(owner.CredentialId);

        var missingResponse = await otherClient.GetAsync($"/api/uploads/{Guid.NewGuid()}");
        var foreignResponse = await otherClient.GetAsync($"/api/uploads/{ownerReservation}");
        var ownerlessFileId = await UploadLegacyAdminFileAsync(adminClient);
        var ownerlessResponse = await otherClient.GetAsync($"/api/uploads/{ownerlessFileId}");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ownerlessResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missingResponse.Content.ReadAsStringAsync()).Should().Be(await foreignResponse.Content.ReadAsStringAsync());
        (await foreignResponse.Content.ReadAsStringAsync()).Should().Be(await ownerlessResponse.Content.ReadAsStringAsync());

        var foreignShare = await otherClient.PostAsJsonAsync("/api/shares/", CreateShareRequest(ownerReservation));
        foreignShare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ownShare = await ownerClient.PostAsJsonAsync("/api/shares/", CreateShareRequest(ownerReservation));
        ownShare.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareResult = await ownShare.Content.ReadFromJsonAsync<CreateShareResult>(JsonOptions);
        var storedShare = await fixture.Services.GetRequiredService<IShareMetadataRepository>()
                                       .GetAsync(shareResult!.ShareId, CancellationToken.None);
        storedShare!.OwnerCredentialId.Should().Be(owner.CredentialId);

        var adminInspection = await adminClient.GetAsync($"/api/uploads/{ownerReservation}");
        adminInspection.StatusCode.Should().Be(HttpStatusCode.OK);
        var inspectionJson = await adminInspection.Content.ReadAsStringAsync();
        inspectionJson.Should().NotContainAny("blobKey", "ownerCredentialId", "isReserved", "isClaimed");
        using var inspectionDocument = JsonDocument.Parse(inspectionJson);
        inspectionDocument.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "fileId", "originalFileName", "plaintextLength", "encryptedLength", "contentType",
            "encryptionFormatVersion", "algorithmId", "chunkSize", "chunkCount", "kdfSaltBase64", "plaintextSha256");
    }

    [TestCase(false, null, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, null, true)]
    public async Task UploadExposure_ShouldBeIndependentlyConfigurable_AndInheritAdminWhenOmitted(
        Boolean enableAdminOperations,
        Boolean? enableUploads,
        Boolean expectMapped)
    {
        await using var fixture = new ScopedApiFactory(enableAdminOperations, enableUploads);
        using var client = fixture.CreateClientWithToken("invalid");

        var response = await client.GetAsync("/api/uploads/capabilities");

        response.StatusCode.Should().Be(expectMapped ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound);
    }

    private static async Task<Credential> CreateCredentialAsync(HttpClient adminClient,
                                                                String name,
                                                                Int64? maxEncryptedFileBytes = null,
                                                                Int64? maxEncryptedShareBytes = null)
    {
        var response = await adminClient.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = name,
            MaxEncryptedFileBytes = maxEncryptedFileBytes,
            MaxEncryptedShareBytes = maxEncryptedShareBytes
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new(document.RootElement.GetProperty("credential").GetProperty("credentialId").GetGuid(),
                   document.RootElement.GetProperty("token").GetString()!);
    }

    private static CreateShareRequest CreateShareRequest(Guid fileId) =>
        new(DateTimeOffset.UtcNow.AddDays(1), [new(fileId)], GenerateDownloadBearerToken: false);

    private static MultipartFormDataContent CreateUploadContent(Guid fileId)
    {
        var metadata = new
        {
            FileId = fileId,
            OriginalFileName = "cipher.bin",
            PlaintextLength = 128,
            EncryptedLength = 160,
            ContentType = "application/octet-stream",
            EncryptionFormatVersion = FormatConstants.EncryptionFormatVersion,
            AlgorithmId = FormatConstants.Aes256GcmAlgorithmId,
            ChunkSize = 64,
            ChunkCount = 2,
            KdfSalt = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (Byte)x).ToArray()),
            PlaintextSha256 = new String('a', 64)
        };
        var content = new MultipartFormDataContent();
        var metadataContent = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
        metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        content.Add(metadataContent, "metadata");
        var encryptedContent = new ByteArrayContent(Enumerable.Range(0, 160).Select(x => (Byte)x).ToArray());
        encryptedContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(encryptedContent, "content", "cipher.bin");
        return content;
    }

    private static void ExpireCredential(String databasePath, Guid credentialId)
    {
        using var database = new LiteDatabase(databasePath);
        var collection = database.GetCollection("upload_credentials");
        var document = collection.FindById(credentialId);
        document["ExpiresAtUnixTimeMilliseconds"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        collection.Update(document);
    }

    private static async Task<Guid> ReserveAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/uploads/reservations", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<UploadReservationResult>(JsonOptions))!.FileId;
    }

    private static async Task UploadAsync(HttpClient client, Guid fileId)
    {
        using var content = CreateUploadContent(fileId);
        (await client.PostAsync("/api/uploads", content)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<Guid> UploadLegacyAdminFileAsync(HttpClient adminClient)
    {
        var reservationResponse = await adminClient.PostAsync("/api/admin/uploads/reservations", null);
        var reservation = await reservationResponse.Content.ReadFromJsonAsync<UploadReservationResult>(JsonOptions);
        using var content = CreateUploadContent(reservation!.FileId);
        (await adminClient.PostAsync("/api/admin/uploads", content)).StatusCode.Should().Be(HttpStatusCode.Created);
        return reservation.FileId;
    }

    private sealed record Credential(Guid CredentialId, String Token);

    private sealed class ScopedApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<String, String?> _previousValues = [];
        private readonly String _rootDirectory;

        public ScopedApiFactory(Boolean enableAdminOperations = true, Boolean? enableUploads = null)
        {
            _rootDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts", $"scoped-upload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
            MetadataDatabasePath = Path.Combine(_rootDirectory, "metadata", "shadowdrop.db");
            LocalStorageRoot = Path.Combine(_rootDirectory, "storage");
            SetEnvironmentVariable("SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN", enableAdminOperations ? BootstrapToken : null);
            SetEnvironmentVariable("ShadowDrop__Metadata__LiteDbPath", MetadataDatabasePath);
            SetEnvironmentVariable("ShadowDrop__Storage__LocalRoot", LocalStorageRoot);
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableAdminOperations", enableAdminOperations ? "true" : "false");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableUploads", enableUploads?.ToString());
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnablePublicDownloads", "true");
        }

        public String BootstrapToken => "scoped-test-bootstrap-token";

        public String LocalStorageRoot { get; }

        public String MetadataDatabasePath { get; }

        public HttpClient CreateClientWithToken(String token)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            return client;
        }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                foreach (var (key, value) in _previousValues)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            base.Dispose(disposing);
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        private void SetEnvironmentVariable(String key, String? value)
        {
            _previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
