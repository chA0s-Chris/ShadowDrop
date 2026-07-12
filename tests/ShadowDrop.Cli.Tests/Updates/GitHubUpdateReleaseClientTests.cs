// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text;

public sealed class GitHubUpdateReleaseClientTests
{
    [Test]
    public async Task GetLatestStableVersionAsync_ShouldPropagateCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new HangingHttpMessageHandler());
        var client = new GitHubUpdateReleaseClient(httpClient);

        var pendingCheck = client.GetLatestStableVersionAsync(cancellation.Token);
        await cancellation.CancelAsync();

        var action = async () => await pendingCheck;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestCase("""{"tag_name":"v1.4.2","draft":true,"prerelease":false}""", TestName = "draft release")]
    [TestCase("""{"tag_name":"v1.4.2-rc.1","draft":false,"prerelease":true}""", TestName = "prerelease release")]
    [TestCase("""{"draft":false,"prerelease":false}""", TestName = "missing tag")]
    [TestCase("""{"tag_name":"not-a-version","draft":false,"prerelease":false}""", TestName = "malformed tag")]
    [TestCase("null", TestName = "null payload")]
    public async Task GetLatestStableVersionAsync_ShouldRejectUnexpectedReleaseData(String payload)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateJsonResponse(payload)));
        var client = new GitHubUpdateReleaseClient(httpClient);

        var action = async () => await client.GetLatestStableVersionAsync(CancellationToken.None);

        (await action.Should().ThrowAsync<UpdateCheckException>()).Which.Message.Should().Contain("github.com/chA0s-Chris/ShadowDrop/releases");
    }

    [Test]
    public async Task GetLatestStableVersionAsync_ShouldReportHttpStatus_WhenReleaseServiceFails()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.InternalServerError)));
        var client = new GitHubUpdateReleaseClient(httpClient);

        var action = async () => await client.GetLatestStableVersionAsync(CancellationToken.None);

        (await action.Should().ThrowAsync<UpdateCheckException>()).Which.Message.Should().Contain("HTTP 500");
    }

    [Test]
    public async Task GetLatestStableVersionAsync_ShouldReportMalformedResponse_WhenBodyIsNotJson()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateJsonResponse("{ not json")));
        var client = new GitHubUpdateReleaseClient(httpClient);

        var action = async () => await client.GetLatestStableVersionAsync(CancellationToken.None);

        (await action.Should().ThrowAsync<UpdateCheckException>()).Which.Message.Should().Contain("malformed");
    }

    [Test]
    public async Task GetLatestStableVersionAsync_ShouldReportNetworkFailure()
    {
        using var httpClient = new HttpClient(StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused")));
        var client = new GitHubUpdateReleaseClient(httpClient);

        var action = async () => await client.GetLatestStableVersionAsync(CancellationToken.None);

        (await action.Should().ThrowAsync<UpdateCheckException>()).Which.Message.Should().Contain("connection refused");
    }

    [Test]
    public async Task GetLatestStableVersionAsync_ShouldReturnParsedVersion_AndSendUserAgent()
    {
        HttpRequestMessage? observedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return CreateJsonResponse("""{"tag_name":"v1.4.2","draft":false,"prerelease":false}""");
        }));
        var client = new GitHubUpdateReleaseClient(httpClient);

        var version = await client.GetLatestStableVersionAsync(CancellationToken.None);

        version.ToString().Should().Be("1.4.2");
        observedRequest!.RequestUri.Should().Be(new Uri("https://api.github.com/repos/chA0s-Chris/ShadowDrop/releases/latest"));
        observedRequest.Headers.TryGetValues("User-Agent", out var userAgentValues).Should().BeTrue();
        userAgentValues!.Single().Should().StartWith("ShadowDrop-CLI/");
    }

    [Test]
    public async Task GetLatestStableVersionAsync_ShouldTimeOut_WhenReleaseServiceHangs()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-12T12:00:00Z"));
        using var httpClient = new HttpClient(new HangingHttpMessageHandler());
        var client = new GitHubUpdateReleaseClient(httpClient, timeProvider);

        var pendingCheck = client.GetLatestStableVersionAsync(CancellationToken.None);
        timeProvider.Advance(GitHubUpdateReleaseClient.RequestTimeout + TimeSpan.FromSeconds(1));

        var action = async () => await pendingCheck;
        (await action.Should().ThrowAsync<UpdateCheckException>()).Which.Message.Should().Contain("timed out");
    }

    private static HttpResponseMessage CreateJsonResponse(String payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private sealed class HangingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
