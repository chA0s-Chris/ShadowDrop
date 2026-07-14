// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Admin;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Infrastructure.Security;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

[TestFixture]
[NonParallelizable]
public sealed class UploadCredentialEndpointTests
{
    [Test]
    public async Task Create_ShouldDiscloseTokenOnce_AndNeverExposeSecretMaterial()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var client = fixture.CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = "automation",
            MaxEncryptedFileBytes = 1024,
            MaxEncryptedShareBytes = 4096
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var created = JsonDocument.Parse(createJson);
        var token = created.RootElement.GetProperty("token").GetString();
        UploadCredentialToken.TryParse(token, out _, out _).Should().BeTrue();
        var credential = created.RootElement.GetProperty("credential");
        var credentialId = credential.GetProperty("credentialId").GetGuid();
        credential.GetProperty("name").GetString().Should().Be("automation");
        credential.GetProperty("capability").GetString().Should().Be("upload-and-share");
        credential.GetProperty("maxEncryptedFileBytes").GetInt64().Should().Be(1024);
        credential.GetProperty("maxEncryptedShareBytes").GetInt64().Should().Be(4096);
        AssertNoSecretMaterial(createJson, true);

        var inspectResponse = await client.GetAsync($"/api/admin/upload-credentials/{credentialId}");
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var inspectJson = await inspectResponse.Content.ReadAsStringAsync();
        AssertNoSecretMaterial(inspectJson, false);
        inspectJson.Should().NotContain(token![..12], "the plaintext token must be unrecoverable after creation");

        var listResponse = await client.GetAsync("/api/admin/upload-credentials/");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertNoSecretMaterial(await listResponse.Content.ReadAsStringAsync(), false);
    }

    [Test]
    public async Task Create_ShouldRejectInvalidRequests()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var client = fixture.CreateAuthenticatedClient();

        var emptyName = await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = "   "
        });
        var nonPositiveLimit = await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = "valid",
            MaxEncryptedFileBytes = 0
        });
        var pastExpiration = await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = "valid",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        emptyName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        nonPositiveLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        pastExpiration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreatedToken_ShouldAuthenticate_UntilRevoked()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var client = fixture.CreateAuthenticatedClient();
        var (credentialId, token) = await CreateCredentialAsync(client, "lifecycle");
        var credentialService = fixture.Services.GetRequiredService<UploadCredentialService>();

        var context = await credentialService.AuthenticateAsync(token, CancellationToken.None);
        context!.CredentialId.Should().Be(credentialId);

        var revokeResponse = await client.PostAsync($"/api/admin/upload-credentials/{credentialId}/revoke", null);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await credentialService.AuthenticateAsync(token, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task List_ShouldPaginateNewestFirst_AndValidateParameters()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var client = fixture.CreateAuthenticatedClient();
        var createdIds = new List<Guid>();
        foreach (var name in new[]
                 {
                     "first",
                     "second",
                     "third"
                 })
        {
            createdIds.Add((await CreateCredentialAsync(client, name)).CredentialId);
        }

        var firstPage = await ReadListAsync(client, "/api/admin/upload-credentials/?limit=2");
        firstPage.Ids.Should().HaveCount(2);
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

        var secondPage = await ReadListAsync(client,
                                             $"/api/admin/upload-credentials/?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        secondPage.Ids.Should().HaveCount(1);
        secondPage.NextCursor.Should().BeNull();
        firstPage.Ids.Concat(secondPage.Ids).Should().OnlyHaveUniqueItems();
        firstPage.Ids.Concat(secondPage.Ids).Should().BeEquivalentTo(createdIds);

        (await client.GetAsync("/api/admin/upload-credentials/?cursor=%20broken%20")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/admin/upload-credentials/?limit=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Revoke_ShouldBeIdempotent_AndKeepTheFirstTimestamp()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var client = fixture.CreateAuthenticatedClient();
        var (credentialId, _) = await CreateCredentialAsync(client, "revocable");

        (await client.PostAsync($"/api/admin/upload-credentials/{credentialId}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var firstRevokedAt = await ReadRevokedAtAsync(client, credentialId);
        firstRevokedAt.Should().NotBeNull();

        (await client.PostAsync($"/api/admin/upload-credentials/{credentialId}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadRevokedAtAsync(client, credentialId)).Should().Be(firstRevokedAt);

        (await client.PostAsync($"/api/admin/upload-credentials/{Guid.NewGuid()}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/admin/upload-credentials/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Routes_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new CredentialTestApiFactory();
        using var unauthenticated = fixture.CreateClient();
        using var wrongToken = fixture.CreateClient();
        wrongToken.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");

        foreach (var client in new[]
                 {
                     unauthenticated,
                     wrongToken
                 })
        {
            (await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
            {
                Name = "denied"
            })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/admin/upload-credentials/")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync($"/api/admin/upload-credentials/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsync($"/api/admin/upload-credentials/{Guid.NewGuid()}/revoke", null))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private static void AssertNoSecretMaterial(String responseJson, Boolean expectToken)
    {
        responseJson.Should().NotContainAny("selectorDigest", "secretHash", "secretSalt", "iterations", "hashVersion");
        if (!expectToken)
        {
            responseJson.Should().NotContain("\"token\"");
        }
    }

    private static async Task<(Guid CredentialId, String Token)> CreateCredentialAsync(HttpClient client, String name)
    {
        var response = await client.PostAsJsonAsync("/api/admin/upload-credentials/", new
        {
            Name = name
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("credential").GetProperty("credentialId").GetGuid(),
                document.RootElement.GetProperty("token").GetString()!);
    }

    private static async Task<(IReadOnlyList<Guid> Ids, String? NextCursor)> ReadListAsync(HttpClient client, String url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = document.RootElement.GetProperty("credentials")
                          .EnumerateArray()
                          .Select(x => x.GetProperty("credentialId").GetGuid())
                          .ToList();
        var nextCursor = document.RootElement.TryGetProperty("nextCursor", out var cursorProperty)
                         && cursorProperty.ValueKind == JsonValueKind.String
            ? cursorProperty.GetString()
            : null;
        return (ids, nextCursor);
    }

    private static async Task<DateTimeOffset?> ReadRevokedAtAsync(HttpClient client, Guid credentialId)
    {
        var response = await client.GetAsync($"/api/admin/upload-credentials/{credentialId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var revokedAt = document.RootElement.GetProperty("revokedAtUtc");
        return revokedAt.ValueKind == JsonValueKind.Null ? null : revokedAt.GetDateTimeOffset();
    }

    // Program reads configuration overrides from environment variables (same mechanism as ApiWalkingSkeletonTests'
    // TestApiFactory); the fixture is [NonParallelizable], so mutating and restoring them per boot is safe.
    private sealed class CredentialTestApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<String, String?> _previousValues = [];
        private readonly String _rootDirectory;

        public CredentialTestApiFactory()
        {
            _rootDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts", $"upload-credential-endpoints-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
            SetEnvironmentVariable("SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN", BootstrapToken);
            SetEnvironmentVariable("ShadowDrop__Metadata__LiteDbPath", Path.Combine(_rootDirectory, "metadata", "shadowdrop.db"));
            SetEnvironmentVariable("ShadowDrop__Storage__LocalRoot", Path.Combine(_rootDirectory, "storage"));
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableAdminOperations", "true");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnablePublicDownloads", "false");
        }

        public String BootstrapToken => "test-bootstrap-token";

        public HttpClient CreateAuthenticatedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", BootstrapToken);
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
