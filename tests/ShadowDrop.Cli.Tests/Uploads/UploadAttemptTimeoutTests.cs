// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;

public sealed class UploadAttemptTimeoutTests
{
    [Test]
    public void InactivityTimeout_ShouldBeFixedAtTenMinutes()
    {
        UploadAttemptTimeout.InactivityTimeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Test]
    public void Reset_ShouldDeferInactivityExpiration()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var sut = new UploadAttemptTimeout(CancellationToken.None, timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(9));
        sut.Reset();
        timeProvider.Advance(TimeSpan.FromMinutes(9));

        sut.Token.IsCancellationRequested.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        sut.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void Token_ShouldCancel_WhenInactivityTimeoutElapses()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var sut = new UploadAttemptTimeout(CancellationToken.None, timeProvider);

        timeProvider.Advance(UploadAttemptTimeout.InactivityTimeout);

        sut.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void Token_ShouldObserveCallerCancellationImmediately()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var sut = new UploadAttemptTimeout(callerCancellation.Token, TimeProvider.System);

        callerCancellation.Cancel();

        sut.Token.IsCancellationRequested.Should().BeTrue();
    }
}
