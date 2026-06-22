// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;

public sealed class ShareExpirationTests
{
    [TestCase("30m", 30 * 60)]
    [TestCase("12h", 12 * 60 * 60)]
    [TestCase("7d", 7 * 24 * 60 * 60)]
    public void TryParse_ShouldParseSupportedUnits(String value, Int64 expectedSeconds)
    {
        ShareExpiration.TryParse(value, out var duration).Should().BeTrue();
        duration.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("soon")]
    [TestCase("7")]
    [TestCase("7w")]
    [TestCase("0d")]
    [TestCase("-3d")]
    [TestCase("9999999999d")]
    public void TryParse_ShouldRejectInvalidOrOutOfRangeValues(String? value)
    {
        ShareExpiration.TryParse(value, out var duration).Should().BeFalse();
        duration.Should().Be(TimeSpan.Zero);
    }
}
