// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;

public sealed class CliConfigurationResolverTlsTests
{
    [TestCase("1")]
    [TestCase("true")]
    [TestCase("TRUE")]
    [TestCase("Yes")]
    [TestCase(" yes ")]
    public void ResolveTls_ShouldBeInsecure_WhenEnvironmentVariableIsTruthy(String value)
    {
        var resolver = CreateResolver(new Dictionary<String, String?>
        {
            ["SHADOWDROP_INSECURE"] = value
        });

        resolver.ResolveTls(null, false).Insecure.Should().BeTrue();
    }

    [Test]
    public void ResolveTls_ShouldBeInsecure_WhenFlagIsPresent()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>());

        resolver.ResolveTls(null, true).Insecure.Should().BeTrue();
    }

    [Test]
    public void ResolveTls_ShouldFallBackToCaCertEnvironmentVariable_WhenFlagMissing()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>
        {
            ["SHADOWDROP_CACERT"] = "/etc/env-ca.pem"
        });

        var options = resolver.ResolveTls(null, false);

        options.CaCertPath.Should().Be("/etc/env-ca.pem");
    }

    [TestCase("0")]
    [TestCase("false")]
    [TestCase("no")]
    [TestCase("")]
    [TestCase("disabled")]
    public void ResolveTls_ShouldNotBeInsecure_WhenEnvironmentVariableIsNotTruthy(String value)
    {
        var resolver = CreateResolver(new Dictionary<String, String?>
        {
            ["SHADOWDROP_INSECURE"] = value
        });

        resolver.ResolveTls(null, false).Insecure.Should().BeFalse();
    }

    [Test]
    public void ResolveTls_ShouldPreferCaCertFlagOverEnvironment()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>
        {
            ["SHADOWDROP_CACERT"] = "/etc/env-ca.pem"
        });

        var options = resolver.ResolveTls("/flag-ca.pem", false);

        options.CaCertPath.Should().Be("/flag-ca.pem");
        options.Insecure.Should().BeFalse();
    }

    [Test]
    public void ResolveTls_ShouldReportNoCaCert_WhenNeitherFlagNorEnvironmentSet()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>());

        var options = resolver.ResolveTls(null, false);

        options.CaCertPath.Should().BeNull();
        options.Insecure.Should().BeFalse();
    }

    [Test]
    public void ResolveTls_ShouldStayInsecure_WhenEnvironmentTruthyEvenWithoutFlag()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>
        {
            ["SHADOWDROP_INSECURE"] = "yes"
        });

        // There is intentionally no flag that can force-disable a truthy environment variable.
        resolver.ResolveTls(null, false).Insecure.Should().BeTrue();
    }

    [Test]
    public void ResolveTls_ShouldTrimCaCertValue()
    {
        var resolver = CreateResolver(new Dictionary<String, String?>());

        var options = resolver.ResolveTls("  /flag-ca.pem  ", false);

        options.CaCertPath.Should().Be("/flag-ca.pem");
    }

    private static CliConfigurationResolver CreateResolver(IReadOnlyDictionary<String, String?> environment) =>
        new(new StubConfigPathResolver(), new StubEnvironmentReader(environment));

    private sealed class StubConfigPathResolver : CliConfigPathResolver
    {
        public override String? GetConfigFilePath() => null;
    }

    private sealed class StubEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => values.GetValueOrDefault(variableName);
    }
}
