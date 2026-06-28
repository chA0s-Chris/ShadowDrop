// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Tests.Fakes;
using System.Net;

public sealed class RevokeShareApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test");

    [Test]
    public async Task RevokeAsync_ShouldPostToAdminEndpoint_WithBearerToken()
    {
        var shareId = Guid.NewGuid();
        var client = new RevokeShareApiClient(new(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri(ServerUrl, $"/api/admin/shares/{shareId}/revoke"));
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("upload-token");
            return new(HttpStatusCode.NoContent);
        })));

        await client.RevokeAsync(ServerUrl, "upload-token", shareId, CancellationToken.None);
    }

    [TestCase(HttpStatusCode.Unauthorized, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.Forbidden, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.NotFound, "Share not found.")]
    [TestCase(HttpStatusCode.BadRequest, "Share revocation failed.")]
    [TestCase(HttpStatusCode.InternalServerError, "Share revocation failed.")]
    public async Task RevokeAsync_ShouldThrowMappedMessage_ForErrorStatusCodes(HttpStatusCode statusCode, String expectedMessage)
    {
        var client = CreateClient(new(statusCode));

        var act = () => client.RevokeAsync(ServerUrl, "upload-token", Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<RevokeShareCommandException>()).WithMessage(expectedMessage);
    }

    [Test]
    public async Task RevokeAsync_ShouldThrowServerConnectionFailed_WhenTransportFails()
    {
        var client = new RevokeShareApiClient(new(StubHttpMessageHandler.Throwing(new HttpRequestException("boom"))));

        var act = () => client.RevokeAsync(ServerUrl, "upload-token", Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<RevokeShareCommandException>()).WithMessage("Server connection failed.");
    }

    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.NoContent)]
    public async Task RevokeAsync_ShouldTreatSuccessStatusCodesAsSuccess(HttpStatusCode statusCode)
    {
        var client = CreateClient(new(statusCode));

        await FluentActions.Awaiting(() => client.RevokeAsync(ServerUrl, "upload-token", Guid.NewGuid(), CancellationToken.None))
                           .Should().NotThrowAsync();
    }

    [Test]
    public async Task RevokeAsync_ShouldValidateArguments()
    {
        var client = CreateClient(new(HttpStatusCode.NoContent));

        await FluentActions.Awaiting(() => client.RevokeAsync(null!, "token", Guid.NewGuid(), CancellationToken.None))
                           .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.RevokeAsync(ServerUrl, " ", Guid.NewGuid(), CancellationToken.None))
                           .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.RevokeAsync(ServerUrl, "token", Guid.Empty, CancellationToken.None))
                           .Should().ThrowAsync<ArgumentException>();
    }

    private static RevokeShareApiClient CreateClient(HttpResponseMessage response) =>
        new(new(new StubHttpMessageHandler(response)));
}
