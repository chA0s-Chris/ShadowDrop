// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Tests.Fakes;
using System.Net;

[NonParallelizable]
public sealed class CliApplicationUpdateTests
{
    private static readonly FixedTerminalCapabilityProvider Interactive =
        new(new(false, false, false));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");

    [Test]
    public async Task InvokeAsync_ShouldNotifyAfterOrdinaryCommand_WhenCachedCheckIndicatesNewerRelease()
    {
        var releaseClient = new StubUpdateReleaseClient("v99.0.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(1), "99.0.0"));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        var shareId = Guid.NewGuid();
        // ReSharper disable once AccessToDisposedClosure
        var services = CreateServices(standardOut, standardError, releaseClient, cache, () => httpClient);

        var exitCode = await CliApplication.InvokeAsync(["share", "revoke", shareId.ToString(), "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"share-revoked:{shareId}");
        releaseClient.RequestCount.Should().Be(0);
        standardError.ToString().Trim().Should()
                     .Be(
                         $"A newer ShadowDrop release v99.0.0 is available (installed: v{CliVersion.Current}). Run 'shadowdrop update' for update instructions.");
    }

    [TestCase("--version")]
    [TestCase("--help")]
    [TestCase("share")]
    public async Task InvokeAsync_ShouldNotRunAutomaticCheck_ForHelpVersionAndParseFailures(String argument)
    {
        var releaseClient = new StubUpdateReleaseClient("v99.0.0");
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError, releaseClient, cache);

        await CliApplication.InvokeAsync([argument], services, CancellationToken.None);

        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
        standardError.ToString().Should().NotContain("shadowdrop update");
    }

    [Test]
    public async Task InvokeAsync_ShouldNotRunAutomaticCheck_OnTopOfTheUpdateCommand()
    {
        var releaseClient = new StubUpdateReleaseClient("v99.0.0");
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError, releaseClient, new());

        await CliApplication.InvokeAsync(["update", "--no-banner"], services, CancellationToken.None);

        releaseClient.RequestCount.Should().Be(1);
        standardError.ToString().Should().NotContain("shadowdrop update");
    }

    [Test]
    public async Task InvokeAsync_ShouldRouteUpdateCommand_WithoutTouchingTheShadowDropServer()
    {
        var releaseClient = new StubUpdateReleaseClient("v99.0.0");
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut, standardError, releaseClient, new());

        var exitCode = await CliApplication.InvokeAsync(["update", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal(
            $"installed-version:{CliVersion.Current}",
            "latest-version:99.0.0",
            "update-status:update-available",
            "update-command:curl -fsSL https://raw.githubusercontent.com/chA0s-Chris/ShadowDrop/refs/heads/main/install.sh | sh");
    }

    [Test]
    public async Task InvokeAsync_ShouldSuppressAutomaticCheck_ForJsonInvocations()
    {
        var releaseClient = new StubUpdateReleaseClient("v99.0.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(1), "99.0.0"));
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new NeverCalledHandler());
        // ReSharper disable once AccessToDisposedClosure
        var services = CreateServices(new(), standardError, releaseClient, cache, () => httpClient);

        var exitCode = await CliApplication.InvokeAsync(["share", "create", "file-id", "--json", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        releaseClient.RequestCount.Should().Be(0);
        standardError.ToString().Should().NotContain("shadowdrop update");
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         StubUpdateReleaseClient releaseClient,
                                                         InMemoryUpdateCheckCache cache,
                                                         Func<HttpClient>? httpClientFactory = null)
    {
        var timeProvider = new ManualTimeProvider(Now);
        return new(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                   _ => httpClientFactory is null ? throw new AssertionException("HTTP client should not have been created.") : httpClientFactory(),
                   standardOut,
                   standardError,
                   new FakeInteractiveSession(),
                   timeProvider,
                   new PlainDownloadProgressReporterFactory(standardOut, standardError, timeProvider),
                   Interactive)
        {
            UpdateServices = FakeUpdateServices.Create(releaseClient, cache)
        };
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }
}
