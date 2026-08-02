// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Status;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Health;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Status;
using ShadowDrop.Contracts;

public sealed class OperationalStatusServiceTests
{
    [TestCase(false, OperationalStatusReasons.DependencyUnavailable)]
    [TestCase(true, OperationalStatusReasons.DependencyTimeout)]
    public async Task GetAdminAsync_ShouldDegradeStatisticsFailuresWithoutThrowing(Boolean timeout, String expectedReason)
    {
        var provider = timeout
            ? new ManualStatisticsProvider(async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertionException("unreachable");
            })
            : new ManualStatisticsProvider(_ => throw new IOException("provider detail"));
        var service = CreateService(new ManualReadinessCheck(true), provider);
        if (timeout)
        {
            service = CreateService(new ManualReadinessCheck(true), provider, collectionTimeout: TimeSpan.FromMilliseconds(20));
        }

        var status = await service.GetAdminAsync(CancellationToken.None);

        status.Ready.Should().BeFalse();
        status.Reason.Should().Be(expectedReason);
        status.Shares.Should().BeNull();
        status.Storage.CompletedFileCount.Should().BeNull();
        status.Components.Single(component => component.Name == "metadata").Reason.Should().Be(expectedReason);
    }

    [Test]
    public async Task GetAdminAsync_ShouldProjectSafeOperationalState()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-02T12:00:00Z"));
        var cleanup = new CleanupRunStatus();
        cleanup.Record(timeProvider.GetUtcNow(), CleanupRunStatus.Success);
        var service = CreateService(new ManualReadinessCheck(true),
                                    new ManualStatisticsProvider(
                                        new OperationalStatisticsSnapshot(new(2, 350, true), new(1, 2, 3, 4, 5, 6))),
                                    cleanup,
                                    timeProvider);
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(42);

        var status = await service.GetAdminAsync(CancellationToken.None);

        status.ProtocolVersion.Should().Be(OperationalStatusProtocol.CurrentVersion);
        status.Ready.Should().BeTrue();
        status.BuildVersion.Should().NotContain("+");
        status.UptimeSeconds.Should().Be(42);
        status.Providers.Should().Be(new StatusProvidersContract("litedb", "filesystem"));
        status.Storage.Should().Be(new StatusStorageContract(2, 350));
        status.Shares.Should().Be(new StatusSharesContract(1, 2, 3, 4, 5, 6));
        status.Cleanup.LastOutcome.Should().Be(CleanupRunStatus.Success);
        status.ResumableSessions.ActiveCount.Should().BeNull();
        status.ConfigurationWarnings.Should().BeEmpty();
    }

    [Test]
    public async Task GetAdminAsync_ShouldPropagateCallerCancellation()
    {
        var service = CreateService(new ManualReadinessCheck(true),
                                    new ManualStatisticsProvider(async cancellationToken =>
                                    {
                                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                                        throw new AssertionException("unreachable");
                                    }));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await service.GetAdminAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task GetAdminAsync_ShouldSuppressInexactLegacyStorageTotals()
    {
        var service = CreateService(new ManualReadinessCheck(true),
                                    new ManualStatisticsProvider(
                                        new OperationalStatisticsSnapshot(new(null, null, false), new(0, 0, 0, 0, 0, 0))));

        var status = await service.GetAdminAsync(CancellationToken.None);

        status.Storage.CompletedFileCount.Should().BeNull();
        status.Storage.CiphertextBytes.Should().BeNull();
        status.ConfigurationWarnings.Should().Equal(OperationalStatusWarnings.StorageAccountingIncomplete);
    }

    [Test]
    public async Task GetPublicAsync_ShouldWorkWithoutOptionalStatistics_WhenEveryCapabilityIsDisabled()
    {
        var service = CreateService(new ManualReadinessCheck(true), null, options: new()
        {
            ApiExposure = new()
            {
                EnableAdminOperations = false,
                EnableUploads = false,
                EnablePublicDownloads = false
            }
        });

        var status = await service.GetPublicAsync(CancellationToken.None);

        status.Ready.Should().BeTrue();
        status.Capabilities.Should().Be(new StatusCapabilitiesContract(false, false, false, false));
    }

    private static OperationalStatusService CreateService(
        IReadinessCheck readinessCheck,
        IOperationalStatisticsProvider? statisticsProvider,
        CleanupRunStatus? cleanupRunStatus = null,
        TimeProvider? timeProvider = null,
        ShadowDropOptions? options = null,
        TimeSpan? collectionTimeout = null) =>
        new(readinessCheck,
            statisticsProvider is null ? [] : [statisticsProvider],
            cleanupRunStatus ?? new(),
            options ?? new(),
            timeProvider ?? TimeProvider.System)
        {
            CollectionTimeout = collectionTimeout ?? OperationalStatusService.DefaultCollectionTimeout
        };

    private sealed class ManualReadinessCheck(Boolean ready) : IReadinessCheck
    {
        public Task<OperationalReadinessSnapshot> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalReadinessSnapshot(
                                ready,
                                ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable,
                                [
                                    new("metadata",
                                        ready ? OperationalComponentStates.Ready : OperationalComponentStates.NotReady,
                                        ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable),
                                    new("storage", OperationalComponentStates.Ready, OperationalStatusReasons.None)
                                ]));

        public Task<Boolean> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(ready);
    }

    private sealed class ManualStatisticsProvider : IOperationalStatisticsProvider
    {
        private readonly Func<CancellationToken, Task<OperationalStatisticsSnapshot>> _get;

        public ManualStatisticsProvider(OperationalStatisticsSnapshot snapshot)
            : this(_ => Task.FromResult(snapshot)) { }

        public ManualStatisticsProvider(Func<CancellationToken, Task<OperationalStatisticsSnapshot>> get)
        {
            _get = get;
        }

        public Task<OperationalStatisticsSnapshot> GetAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) => _get(cancellationToken);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
