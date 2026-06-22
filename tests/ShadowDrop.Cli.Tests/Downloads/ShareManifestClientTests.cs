// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text;
using System.Text.Json;

public sealed class ShareManifestClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test");

    [Test]
    public async Task GetAsync_ShouldAttachBearerToken_WhenProvided()
    {
        AuthenticationHeaderValueCapture captured = new();
        var manifest = new ShareManifestContract
        {
            Files =
            [
                new()
                {
                    FileId = Guid.NewGuid().ToString()
                }
            ]
        };
        var client = new ShareManifestClient(new(new StubHttpMessageHandler(request =>
        {
            captured.Scheme = request.Headers.Authorization?.Scheme;
            captured.Parameter = request.Headers.Authorization?.Parameter;
            return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(manifest));
        })));

        await client.GetAsync(ServerUrl, "share", "bearer-token", CancellationToken.None);

        captured.Scheme.Should().Be("Bearer");
        captured.Parameter.Should().Be("bearer-token");
    }

    [Test]
    public async Task GetAsync_ShouldReturnManifest_WhenResponseIsValid()
    {
        var manifest = new ShareManifestContract
        {
            Files =
            [
                new()
                {
                    FileId = Guid.NewGuid().ToString(),
                    FileName = "a.bin",
                    Length = 10
                }
            ]
        };
        var client = CreateClient(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(manifest)));

        var result = await client.GetAsync(ServerUrl, "share", null, CancellationToken.None);

        result.Files.Should().HaveCount(1);
    }

    [Test]
    public async Task GetAsync_ShouldThrow_WhenManifestHasNoFiles()
    {
        var manifest = new ShareManifestContract
        {
            Files = []
        };
        var client = CreateClient(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(manifest)));

        var act = () => client.GetAsync(ServerUrl, "share", null, CancellationToken.None);

        (await act.Should().ThrowAsync<DownloadCommandException>()).WithMessage("Share metadata invalid or missing.");
    }

    [Test]
    public async Task GetAsync_ShouldThrow_WhenResponseBodyIsMalformedJson()
    {
        var client = CreateClient(JsonResponse(HttpStatusCode.OK, "{ not json"));

        var act = () => client.GetAsync(ServerUrl, "share", null, CancellationToken.None);

        (await act.Should().ThrowAsync<DownloadCommandException>()).WithMessage("Share metadata invalid or missing.");
    }

    [TestCase(HttpStatusCode.NotFound, "Share unavailable or unauthorized.")]
    [TestCase(HttpStatusCode.Unauthorized, "Share unavailable or unauthorized.")]
    [TestCase(HttpStatusCode.Forbidden, "Download authorization failed.")]
    [TestCase(HttpStatusCode.InternalServerError, "Server connection failed.")]
    public async Task GetAsync_ShouldThrowMappedMessage_ForErrorStatusCodes(HttpStatusCode statusCode, String expectedMessage)
    {
        var client = CreateClient(new(statusCode));

        var act = () => client.GetAsync(ServerUrl, "share", null, CancellationToken.None);

        (await act.Should().ThrowAsync<DownloadCommandException>()).WithMessage(expectedMessage);
    }

    [Test]
    public async Task GetAsync_ShouldThrowServerConnectionFailed_WhenTransportFails()
    {
        var client = new ShareManifestClient(new(StubHttpMessageHandler.Throwing(new HttpRequestException("boom"))));

        var act = () => client.GetAsync(ServerUrl, "share", null, CancellationToken.None);

        (await act.Should().ThrowAsync<DownloadCommandException>()).WithMessage("Server connection failed.");
    }

    private static ShareManifestClient CreateClient(HttpResponseMessage response) =>
        new(new(new StubHttpMessageHandler(response)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, String body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class AuthenticationHeaderValueCapture
    {
        public String? Parameter { get; set; }
        public String? Scheme { get; set; }
    }
}
