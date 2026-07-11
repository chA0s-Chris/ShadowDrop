// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Http;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Http;

public sealed class ControlPlaneTimeoutTests
{
    [Test]
    public void Token_ShouldCancel_WhenTotalDeadlineElapses()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var sut = new ControlPlaneTimeout(CancellationToken.None, timeProvider);

        timeProvider.Advance(ControlPlaneTimeout.TotalTimeout);

        sut.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void Token_ShouldObserveCallerCancellationImmediately()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var sut = new ControlPlaneTimeout(callerCancellation.Token);

        callerCancellation.Cancel();

        sut.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void TotalTimeout_ShouldBeFixedAt100Seconds()
    {
        ControlPlaneTimeout.TotalTimeout.Should().Be(TimeSpan.FromSeconds(100));
    }
}
