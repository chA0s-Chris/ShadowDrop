// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads.Progress;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads.Progress;

public sealed class HumanReadableSizeTests
{
    [Test]
    public void FormatBytes_ShouldClampNegativeValuesToZero()
    {
        HumanReadableSize.FormatBytes(-5).Should().Be("0 B");
    }

    [Test]
    public void FormatBytes_ShouldRollOverToNextUnit_WhenValueRoundsUpAtUnitBoundary()
    {
        // 999_960 bytes is 999.96 KB, which rounds to "1000.0 KB" at one decimal place; it should read "1.0 MB" instead.
        HumanReadableSize.FormatBytes(999_960).Should().Be("1.0 MB");
    }

    [TestCase(0, "0 B")]
    [TestCase(64, "64 B")]
    [TestCase(999, "999 B")]
    [TestCase(1000, "1.0 KB")]
    [TestCase(128_400_000, "128.4 MB")]
    [TestCase(1_400_000_000, "1.4 GB")]
    public void FormatBytes_ShouldUseDecimalUnitsWithOneDecimalPlace(Int64 bytes, String expected)
    {
        HumanReadableSize.FormatBytes(bytes).Should().Be(expected);
    }

    [Test]
    public void FormatDuration_ShouldUseHoursAndMinutes()
    {
        HumanReadableSize.FormatDuration(TimeSpan.FromMinutes(65)).Should().Be("1h 5m");
    }

    [Test]
    public void FormatDuration_ShouldUseMinutesAndSeconds()
    {
        HumanReadableSize.FormatDuration(TimeSpan.FromSeconds(184)).Should().Be("3m 4s");
    }

    [TestCase(2.1, "2.1s")]
    [TestCase(0, "0.0s")]
    public void FormatDuration_ShouldUseSecondsBelowOneMinute(Double seconds, String expected)
    {
        HumanReadableSize.FormatDuration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Test]
    public void FormatSpeed_ShouldDeriveDecimalRatePerSecond()
    {
        HumanReadableSize.FormatSpeed(128_400_000, TimeSpan.FromSeconds(2)).Should().Be("64.2 MB/s");
    }

    [Test]
    public void FormatSpeed_ShouldGuardAgainstZeroElapsedTime()
    {
        HumanReadableSize.FormatSpeed(1000, TimeSpan.Zero).Should().EndWith("/s");
    }

    [Test]
    public void FormatSpeed_ShouldRollOverToNextUnit_WhenValueRoundsUpAtUnitBoundary()
    {
        // 999_960 bytes over 1 second is 999.96 KB/s, which rounds to "1000.0 KB/s"; it should read "1.0 MB/s" instead.
        HumanReadableSize.FormatSpeed(999_960, TimeSpan.FromSeconds(1)).Should().Be("1.0 MB/s");
    }
}
