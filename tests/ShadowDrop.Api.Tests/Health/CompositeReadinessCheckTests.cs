// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Health;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Health;

[TestFixture]
public sealed class CompositeReadinessCheckTests
{
    [Test]
    public async Task IsReadyAsync_ShouldReportReady_WhenEveryDependencyIsReady()
    {
        var first = new RecordingReadinessDependencyCheck(true);
        var second = new RecordingReadinessDependencyCheck(true);
        var check = new CompositeReadinessCheck([first, second]);

        (await check.IsReadyAsync(CancellationToken.None)).Should().BeTrue();
        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(1);
    }

    [Test]
    public async Task IsReadyAsync_ShouldStopAtFirstUnreadyDependency()
    {
        var first = new RecordingReadinessDependencyCheck(false);
        var second = new RecordingReadinessDependencyCheck(true);
        var check = new CompositeReadinessCheck([first, second]);

        (await check.IsReadyAsync(CancellationToken.None)).Should().BeFalse();
        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(0);
    }

    private sealed class RecordingReadinessDependencyCheck(Boolean result) : IReadinessDependencyCheck
    {
        public Int32 CallCount { get; private set; }

        public Task<Boolean> IsReadyAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
