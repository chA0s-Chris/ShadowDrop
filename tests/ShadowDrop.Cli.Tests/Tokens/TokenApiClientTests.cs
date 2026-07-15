// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Tokens;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Tokens;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text.Json;

public sealed class TokenApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test");

    [Test]
    public async Task CreateAndListAsync_ShouldReportMissingManagementEndpoint()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NotFound)));
        var apiClient = new TokenApiClient(httpClient);

        var create = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await apiClient.CreateAsync(ServerUrl, "admin-token", new("automation", null, null, null), CancellationToken.None);
        var list = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await apiClient.ListAsync(ServerUrl, "admin-token", null, null, CancellationToken.None);

        await create.Should().ThrowAsync<TokenCommandException>().WithMessage("Upload credential management endpoint not found.");
        await list.Should().ThrowAsync<TokenCommandException>().WithMessage("Upload credential management endpoint not found.");
    }

    [Test]
    public async Task CreateAsync_ShouldPostRequest_AndParseResult()
    {
        HttpRequestMessage? capturedRequest = null;
        String? capturedPayload = null;
        var credentialId = Guid.NewGuid();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            capturedPayload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new(HttpStatusCode.Created)
            {
                Content = new StringContent(CredentialJson(credentialId, true))
            };
        }));

        var result = await new TokenApiClient(httpClient).CreateAsync(ServerUrl,
                                                                      "admin-token",
                                                                      new("automation", null, 1024, null),
                                                                      CancellationToken.None);

        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.AbsolutePath.Should().Be("/api/admin/upload-credentials/");
        capturedRequest.Headers.Authorization!.ToString().Should().Be("Bearer admin-token");
        using var payload = JsonDocument.Parse(capturedPayload!);
        payload.RootElement.GetProperty("name").GetString().Should().Be("automation");
        payload.RootElement.GetProperty("maxEncryptedFileBytes").GetInt64().Should().Be(1024);
        result.Credential.CredentialId.Should().Be(credentialId);
        result.Token.Should().Be("sdu1.selector.secret");
    }

    [Test]
    public async Task InspectAndRevokeAsync_ShouldReportCredentialNotFound()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NotFound)));
        var apiClient = new TokenApiClient(httpClient);

        var inspect = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await apiClient.InspectAsync(ServerUrl, "admin-token", Guid.NewGuid(), CancellationToken.None);
        var revoke = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await apiClient.RevokeAsync(ServerUrl, "admin-token", Guid.NewGuid(), CancellationToken.None);

        await inspect.Should().ThrowAsync<TokenCommandException>().WithMessage("Upload credential not found.");
        await revoke.Should().ThrowAsync<TokenCommandException>().WithMessage("Upload credential not found.");
    }

    [Test]
    public async Task ListAsync_ShouldEncodeCursorAndLimit()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"credentials":[{{CredentialJson(Guid.NewGuid(), false)}}],"nextCursor":"abc"}""")
            };
        }));

        var result = await new TokenApiClient(httpClient).ListAsync(ServerUrl, "admin-token", "cursor+value", 25, CancellationToken.None);

        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/api/admin/upload-credentials/?cursor=cursor%2Bvalue&limit=25");
        result.Credentials.Should().HaveCount(1);
        result.NextCursor.Should().Be("abc");
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task ListAsync_ShouldForwardEveryNonNullCursor(String cursor)
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"credentials":[],"nextCursor":null}""")
            };
        }));

        _ = await new TokenApiClient(httpClient).ListAsync(ServerUrl, "admin-token", cursor, null, CancellationToken.None);

        capturedRequest!.RequestUri!.Query.Should().Be($"?cursor={Uri.EscapeDataString(cursor)}");
    }

    [Test]
    public async Task RevokeAsync_ShouldAcceptNoContent_AndMapAuthenticationFailures()
    {
        using var successClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        using var unauthorizedClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.Unauthorized)));

        await new TokenApiClient(successClient).RevokeAsync(ServerUrl, "admin-token", Guid.NewGuid(), CancellationToken.None);
        var revokeUnauthorized = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await new TokenApiClient(unauthorizedClient).RevokeAsync(ServerUrl, "admin-token", Guid.NewGuid(), CancellationToken.None);

        await revokeUnauthorized.Should().ThrowAsync<TokenCommandException>().WithMessage("Admin token invalid or missing.");
    }

    [Test]
    public async Task SendAsync_ShouldMapConnectionFailures()
    {
        using var httpClient = new HttpClient(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

        var list = async () =>
            // ReSharper disable once AccessToDisposedClosure
            await new TokenApiClient(httpClient).ListAsync(ServerUrl, "admin-token", null, null, CancellationToken.None);

        await list.Should().ThrowAsync<TokenCommandException>().WithMessage("Server connection failed.");
    }

    private static String CredentialJson(Guid credentialId, Boolean wrapWithToken)
    {
        var credential = $$"""
                           {"credentialId":"{{credentialId}}","name":"automation","createdAtUtc":"2026-07-14T12:00:00Z",
                           "expiresAtUtc":null,"revokedAtUtc":null,"lastUsedAtUtc":null,"capability":"upload-and-share",
                           "maxEncryptedFileBytes":1024,"maxEncryptedShareBytes":null}
                           """;
        return wrapWithToken
            ? $$"""{"credential":{{credential}},"token":"sdu1.selector.secret"}"""
            : credential;
    }
}
