// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text;

public sealed class CreateShareApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test");

    private static CreateShareCliRequest Request => new(DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
                                                        [new(Guid.NewGuid(), "file.bin")],
                                                        false,
                                                        false,
                                                        null);

    [Test]
    public async Task CreateAsync_ShouldReturnResult_WhenServerReturnsCreated()
    {
        const String body = """{"shareToken":"abc","downloadBearerToken":"tok"}""";
        var client = CreateClient(JsonResponse(HttpStatusCode.Created, body));

        var result = await client.CreateAsync(ServerUrl, "upload-token", Request, CancellationToken.None);

        result.ShareToken.Should().Be("abc");
        result.DownloadBearerToken.Should().Be("tok");
    }

    [Test]
    public async Task CreateAsync_ShouldThrow_WhenCreatedBodyIsMalformed()
    {
        var client = CreateClient(JsonResponse(HttpStatusCode.Created, "{ not json"));

        var act = () => client.CreateAsync(ServerUrl, "upload-token", Request, CancellationToken.None);

        (await act.Should().ThrowAsync<CreateShareCommandException>()).WithMessage("Share creation failed.");
    }

    [TestCase(HttpStatusCode.Unauthorized, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.Forbidden, "Authentication token invalid or missing.")]
    [TestCase(HttpStatusCode.BadRequest, "Invalid share request.")]
    [TestCase(HttpStatusCode.InternalServerError, "Server connection failed.")]
    public async Task CreateAsync_ShouldThrowMappedMessage_ForErrorStatusCodes(HttpStatusCode statusCode, String expectedMessage)
    {
        var client = CreateClient(new(statusCode));

        var act = () => client.CreateAsync(ServerUrl, "upload-token", Request, CancellationToken.None);

        (await act.Should().ThrowAsync<CreateShareCommandException>()).WithMessage(expectedMessage);
    }

    [Test]
    public async Task CreateAsync_ShouldThrowServerConnectionFailed_WhenTransportFails()
    {
        var client = new CreateShareApiClient(new(StubHttpMessageHandler.Throwing(new HttpRequestException("boom"))));

        var act = () => client.CreateAsync(ServerUrl, "upload-token", Request, CancellationToken.None);

        (await act.Should().ThrowAsync<CreateShareCommandException>()).WithMessage("Server connection failed.");
    }

    [Test]
    public async Task CreateAsync_ShouldValidateArguments()
    {
        var client = CreateClient(new(HttpStatusCode.Created));

        await FluentActions.Awaiting(() => client.CreateAsync(null!, "token", Request, CancellationToken.None))
                           .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.CreateAsync(ServerUrl, " ", Request, CancellationToken.None))
                           .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => client.CreateAsync(ServerUrl, "token", null!, CancellationToken.None))
                           .Should().ThrowAsync<ArgumentNullException>();
    }

    private static CreateShareApiClient CreateClient(HttpResponseMessage response) =>
        new(new(new StubHttpMessageHandler(response)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, String body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}
