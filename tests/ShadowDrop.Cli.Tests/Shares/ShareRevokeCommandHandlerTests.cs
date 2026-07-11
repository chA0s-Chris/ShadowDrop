// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Tests.Fakes;
using System.Net;

public sealed class ShareRevokeCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ShouldFailBeforeHttpCall_WhenShareIdIsInvalid()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        var handler = new ShareRevokeCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                    httpClient,
                                                    standardOut,
                                                    standardError);

        var exitCode = await handler.ExecuteAsync(new("not-a-guid", null, null), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be("Share id invalid or missing.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldFailWithoutPrintingToken_WhenAuthenticationFails()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.Unauthorized)));
        var handler = new ShareRevokeCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "secret-upload-token"),
                                                    httpClient,
                                                    standardOut,
                                                    standardError);

        var exitCode = await handler.ExecuteAsync(new(Guid.NewGuid().ToString(), null, null), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be("Authentication token invalid or missing.");
        standardError.ToString().Should().NotContain("secret-upload-token");
    }

    [Test]
    public async Task ExecuteAsync_ShouldReportDistinctFailure_WhenShareIsUnknown()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NotFound)));
        var handler = new ShareRevokeCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "secret-upload-token"),
                                                    httpClient,
                                                    standardOut,
                                                    standardError);

        var exitCode = await handler.ExecuteAsync(new(Guid.NewGuid().ToString(), null, null), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be("Share not found.");
        standardError.ToString().Should().NotContain("secret-upload-token");
    }

    [Test]
    public async Task ExecuteAsync_ShouldReportSuccess_WhenShareIsRevoked()
    {
        var shareId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        var handler = new ShareRevokeCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                    httpClient,
                                                    standardOut,
                                                    standardError);

        var exitCode = await handler.ExecuteAsync(new(shareId.ToString(), null, null), CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"share-revoked:{shareId}");
        standardError.ToString().Should().BeEmpty();
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }
}
