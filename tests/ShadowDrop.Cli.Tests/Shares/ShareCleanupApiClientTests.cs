// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Tests.Fakes;
using System.Net;

public sealed class ShareCleanupApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test");

    [Test]
    public async Task CleanupAsync_ShouldPostToAdminEndpoint_WithBearerToken()
    {
        var client = new ShareCleanupApiClient(new(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri(ServerUrl, "/api/admin/shares/cleanup"));
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("upload-token");
            return JsonResponse("""{"candidatesScanned":2,"sharesCompleted":1,"blobsDeleted":3,"blobsAlreadyMissing":4,"failures":5,"skipped":false}""");
        })));

        var result = await client.CleanupAsync(ServerUrl, "upload-token", CancellationToken.None);

        result.Should().Be(new ShareCleanupResultContract(2, 1, 3, 4, 5, false));
    }

    [TestCase(HttpStatusCode.Unauthorized, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.Forbidden, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.NotFound, "Share cleanup endpoint not found.")]
    [TestCase(HttpStatusCode.BadRequest, "Share cleanup failed.")]
    [TestCase(HttpStatusCode.InternalServerError, "Share cleanup failed.")]
    public async Task CleanupAsync_ShouldThrowMappedMessage_ForErrorStatusCodes(HttpStatusCode statusCode, String expectedMessage)
    {
        var client = CreateClient(new(statusCode));

        var act = () => client.CleanupAsync(ServerUrl, "upload-token", CancellationToken.None);

        (await act.Should().ThrowAsync<ShareCleanupCommandException>()).WithMessage(expectedMessage);
    }

    [Test]
    public async Task CleanupAsync_ShouldThrowServerConnectionFailed_WhenTransportFails()
    {
        var client = new ShareCleanupApiClient(new(StubHttpMessageHandler.Throwing(new HttpRequestException("boom"))));

        var act = () => client.CleanupAsync(ServerUrl, "upload-token", CancellationToken.None);

        (await act.Should().ThrowAsync<ShareCleanupCommandException>()).WithMessage("Server connection failed.");
    }

    [Test]
    public async Task CleanupAsync_ShouldValidateArguments()
    {
        var client = CreateClient(
            JsonResponse("""{"candidatesScanned":0,"sharesCompleted":0,"blobsDeleted":0,"blobsAlreadyMissing":0,"failures":0,"skipped":false}"""));

        await FluentActions.Awaiting(() => client.CleanupAsync(null!, "token", CancellationToken.None))
                           .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.CleanupAsync(ServerUrl, " ", CancellationToken.None))
                           .Should().ThrowAsync<ArgumentException>();
    }

    private static ShareCleanupApiClient CreateClient(HttpResponseMessage response) =>
        new(new(new StubHttpMessageHandler(response)));

    private static HttpResponseMessage JsonResponse(String json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
}
