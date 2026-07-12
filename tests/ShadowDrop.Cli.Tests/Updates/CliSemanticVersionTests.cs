// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;

public sealed class CliSemanticVersionTests
{
    [Test]
    public void CompareTo_ShouldFollowSemVerPrecedence()
    {
        // The canonical ordering example from the SemVer 2.0.0 specification, extended with core versions.
        String[] ascending =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "1.0.1",
            "1.1.0",
            "2.0.0"
        ];

        for (var left = 0; left < ascending.Length; left++)
        {
            for (var right = 0; right < ascending.Length; right++)
            {
                CliSemanticVersion.TryParse(ascending[left], out var leftVersion).Should().BeTrue();
                CliSemanticVersion.TryParse(ascending[right], out var rightVersion).Should().BeTrue();

                Math.Sign(leftVersion!.CompareTo(rightVersion)).Should()
                    .Be(Math.Sign(left.CompareTo(right)), $"'{ascending[left]}' vs '{ascending[right]}'");
            }
        }
    }

    [Test]
    public void CompareTo_ShouldIgnoreBuildMetadata()
    {
        CliSemanticVersion.TryParse("1.2.3+build.1", out var left).Should().BeTrue();
        CliSemanticVersion.TryParse("1.2.3+build.2", out var right).Should().BeTrue();

        left!.CompareTo(right).Should().Be(0);
    }

    [Test]
    public void CompareTo_ShouldOrderLongNumericPrereleaseIdentifiersNumerically()
    {
        CliSemanticVersion.TryParse("1.0.0-9999999999999999999", out var left).Should().BeTrue();
        CliSemanticVersion.TryParse("1.0.0-10000000000000000000", out var right).Should().BeTrue();

        left!.CompareTo(right).Should().BeNegative();
    }

    [Test]
    public void CompareTo_ShouldRankAnyVersionAboveNull()
    {
        CliSemanticVersion.TryParse("0.0.0", out var version).Should().BeTrue();

        version!.CompareTo(null).Should().BePositive();
    }

    [Test]
    public void CompareTo_ShouldRankStableAboveItsOwnPrerelease()
    {
        CliSemanticVersion.TryParse("1.2.0", out var stable).Should().BeTrue();
        CliSemanticVersion.TryParse("1.2.0-preview.1", out var prerelease).Should().BeTrue();

        stable!.CompareTo(prerelease).Should().BePositive();
        prerelease!.CompareTo(stable).Should().BeNegative();
        prerelease.IsPrerelease.Should().BeTrue();
        stable.IsPrerelease.Should().BeFalse();
    }

    [TestCase("1.2.3", "1.2.3")]
    [TestCase("v1.2.3", "1.2.3")]
    [TestCase("V1.2.3", "1.2.3")]
    [TestCase("0.0.0", "0.0.0")]
    [TestCase("1.2.3-alpha.1", "1.2.3-alpha.1")]
    [TestCase("1.2.3+build.5", "1.2.3")]
    [TestCase("v1.2.3-rc.1+build.5", "1.2.3-rc.1")]
    [TestCase(" 1.2.3 ", "1.2.3")]
    [TestCase("1.2.3-0.x-y.7", "1.2.3-0.x-y.7")]
    public void TryParse_ShouldAcceptValidVersions(String text, String expectedCanonicalForm)
    {
        var parsed = CliSemanticVersion.TryParse(text, out var version);

        parsed.Should().BeTrue();
        version!.ToString().Should().Be(expectedCanonicalForm);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("v")]
    [TestCase("1")]
    [TestCase("1.2")]
    [TestCase("1.2.3.4")]
    [TestCase("01.2.3")]
    [TestCase("1.02.3")]
    [TestCase("1.2.-3")]
    [TestCase("1.2.3-")]
    [TestCase("1.2.3-alpha..1")]
    [TestCase("1.2.3-01")]
    [TestCase("1.2.3-alpha_1")]
    [TestCase("1.2.3+")]
    [TestCase("1.2.3+bad!")]
    [TestCase("1.2.3+foo+bar")]
    [TestCase("1.2.3+foo..bar")]
    [TestCase("1.2.3+.foo")]
    [TestCase("1.2.3+foo.")]
    [TestCase("abc")]
    [TestCase("vv1.2.3")]
    public void TryParse_ShouldRejectMalformedVersions(String? text)
    {
        var parsed = CliSemanticVersion.TryParse(text, out var version);

        parsed.Should().BeFalse();
        version.Should().BeNull();
    }
}
