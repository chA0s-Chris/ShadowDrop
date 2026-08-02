// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Health;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Health;
using ShadowDrop.Contracts;

[TestFixture]
public sealed class CompositeReadinessCheckTests
{
    [Test]
    public async Task IsReadyAsync_ShouldReportReady_WhenEveryDependencyIsReady()
    {
        var first = new RecordingOperationalDependencyProbe(false, "metadata");
        var second = new RecordingOperationalDependencyProbe(false, "storage");
        var check = new CompositeReadinessCheck([first, second]);

        (await check.IsReadyAsync(CancellationToken.None)).Should().BeTrue();
        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(1);
    }

    [Test]
    public async Task GetStatusAsync_ShouldRunIndependentProbesConcurrently_AndExposeStableFailure()
    {
        var first = new RecordingOperationalDependencyProbe(true, "metadata");
        var second = new RecordingOperationalDependencyProbe(false, "storage");
        var check = new CompositeReadinessCheck([first, second]);

        var result = await check.GetStatusAsync(CancellationToken.None);

        result.Ready.Should().BeFalse();
        result.Reason.Should().Be(OperationalStatusReasons.DependencyUnavailable);
        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(1);
    }

    [TestCase("filesystem")]
    [TestCase("litedb")]
    public async Task GetStatusAsync_ShouldReturnAtDeadline_WhenLocalProbeRemainsBlocked(String provider)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();

        void BlockingProbe()
        {
            entered.Set();
            release.Wait();
            finished.Set();
        }

        IOperationalDependencyProbe probe = provider switch
        {
            "filesystem" => new FileSystemOperationalDependencyProbe(BlockingProbe),
            "litedb" => new LiteDbOperationalDependencyProbe(BlockingProbe),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported provider.")
        };
        var check = new CompositeReadinessCheck([probe])
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };

        try
        {
            var result = await check.GetStatusAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

            entered.IsSet.Should().BeTrue();
            result.Ready.Should().BeFalse();
            result.Reason.Should().Be(OperationalStatusReasons.DependencyTimeout);
            result.Components.Should().ContainSingle(component => component.State == OperationalComponentStates.NotReady
                                                                  && component.Reason == OperationalStatusReasons.DependencyTimeout);
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
    }

    private sealed class RecordingOperationalDependencyProbe(Boolean fail, String component) : IOperationalDependencyProbe
    {
        public Int32 CallCount { get; private set; }
        public IReadOnlyList<String> Components { get; } = [component];

        public String Name => component;

        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return fail ? Task.FromException(new IOException("unavailable")) : Task.CompletedTask;
        }
    }
}
