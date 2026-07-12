// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Terminals;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Tests.Fakes;

public sealed class AutomaticUpdateCheckTests
{
    private static readonly FixedTerminalCapabilityProvider Interactive =
        new(new(false, false, true));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");

    [Test]
    public async Task RunAsync_ShouldContactReleaseSourceAndNotify_WhenCacheIsMissing()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(1);
        cache.Record.Should().Be(new UpdateCheckRecord(Now, "1.5.0"));
        standardError.ToString().Trim().Should()
                     .Be("A newer ShadowDrop release v1.5.0 is available (installed: v1.4.0). Run 'shadowdrop update' for update instructions.");
    }

    [Test]
    public async Task RunAsync_ShouldNotifyFromFreshCache_WithoutContactingReleaseSource()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(23), "1.5.0"));
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
        standardError.ToString().Should().Contain("shadowdrop update");
    }

    [Test]
    public async Task RunAsync_ShouldNotSuppressCheck_WhenOptOutVariableIsFalsy()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var standardError = new StringWriter();

        await RunAsync(releaseClient, new InMemoryUpdateCheckCache(), standardError, environment: new Dictionary<String, String?>
        {
            ["SHADOWDROP_NO_UPDATE_CHECK"] = "0"
        });

        releaseClient.RequestCount.Should().Be(1);
        standardError.ToString().Should().Contain("shadowdrop update");
    }

    [Test]
    public async Task RunAsync_ShouldRecordFailedCheck_AndStaySilent()
    {
        var releaseClient = new StubUpdateReleaseClient(new UpdateCheckException("release service unavailable"));
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(1);
        cache.Record.Should().Be(new UpdateCheckRecord(Now, null));
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_ShouldRefreshExpiredCache()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(24), "1.4.5"));
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(1);
        cache.Record.Should().Be(new UpdateCheckRecord(Now, "1.5.0"));
        standardError.ToString().Should().Contain("v1.5.0");
    }

    [Test]
    public async Task RunAsync_ShouldStaySilent_WhenCachedLatestVersionIsNotNewer()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(1), "1.4.0"));
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_ShouldStaySilent_WhenFreshCacheRecordsAFailedCheck()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache(new(Now - TimeSpan.FromHours(1), null));
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError);

        releaseClient.RequestCount.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_ShouldStaySilent_WhenInstalledPrereleaseIsAheadOfLatestStable()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError, installedVersion: "1.6.0-preview.1");

        standardError.ToString().Should().BeEmpty();
    }

    [TestCase(true, false, TestName = "redirected streams")]
    [TestCase(false, true, TestName = "CI terminal environment")]
    public async Task RunAsync_ShouldSuppressCheck_ForNonInteractiveTerminals(Boolean isRedirected, Boolean isCiEnvironment)
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();
        var terminalCapabilityProvider = new FixedTerminalCapabilityProvider(new(isRedirected, isCiEnvironment, false));

        await RunAsync(releaseClient, cache, standardError, terminalCapabilityProvider);

        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
    }

    [TestCase("SHADOWDROP_NO_UPDATE_CHECK", "1")]
    [TestCase("SHADOWDROP_NO_UPDATE_CHECK", "true")]
    [TestCase("SHADOWDROP_NO_UPDATE_CHECK", "yes")]
    [TestCase("GITHUB_ACTIONS", "true")]
    public async Task RunAsync_ShouldSuppressCheck_ForOptOutAndCiEnvironmentVariables(String variableName, String value)
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var cache = new InMemoryUpdateCheckCache();
        var standardError = new StringWriter();

        await RunAsync(releaseClient, cache, standardError, environment: new Dictionary<String, String?>
        {
            [variableName] = value
        });

        releaseClient.RequestCount.Should().Be(0);
        cache.WriteCount.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_ShouldSwallowUnexpectedFailures()
    {
        var releaseClient = new StubUpdateReleaseClient("v1.5.0");
        var standardError = new StringWriter();

        var action = async () => await RunAsync(releaseClient, new ThrowingUpdateCheckCache(), standardError);

        await action.Should().NotThrowAsync();
        standardError.ToString().Should().BeEmpty();
    }

    private static Task RunAsync(StubUpdateReleaseClient releaseClient,
                                 IUpdateCheckCache cache,
                                 StringWriter standardError,
                                 ITerminalCapabilityProvider? terminalCapabilityProvider = null,
                                 String installedVersion = "1.4.0",
                                 IReadOnlyDictionary<String, String?>? environment = null) =>
        AutomaticUpdateCheck.RunAsync(FakeUpdateServices.Create(releaseClient, cache, environment: environment),
                                      terminalCapabilityProvider ?? Interactive,
                                      standardError,
                                      new ManualTimeProvider(Now),
                                      installedVersion,
                                      CancellationToken.None);

    private sealed class ThrowingUpdateCheckCache : IUpdateCheckCache
    {
        public UpdateCheckRecord Read() => throw new InvalidOperationException("cache exploded");

        public void Write(UpdateCheckRecord record) => throw new InvalidOperationException("cache exploded");
    }
}
