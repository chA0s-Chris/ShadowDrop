// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;

[NonParallelizable]
public sealed class CliConfigPathResolverTests
{
    private String? _originalHome;

    [Test]
    public void GetConfigFilePath_ShouldComposePathBelowHome_WhenHomeIsSet()
    {
        var home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("HOME", home);
        var resolver = new CliConfigPathResolver();

        var result = resolver.GetConfigFilePath();

        result.Should().Be(Path.Combine(home, ".config", "shadowdrop", "config.json"));
    }

    [Test]
    public void GetConfigFilePath_ShouldFallBackToUserProfile_WhenHomeIsBlank()
    {
        Environment.SetEnvironmentVariable("HOME", "   ");
        var expectedHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resolver = new CliConfigPathResolver();

        var result = resolver.GetConfigFilePath();

        if (String.IsNullOrWhiteSpace(expectedHome))
        {
            result.Should().BeNull();
        }
        else
        {
            result.Should().Be(Path.Combine(expectedHome, ".config", "shadowdrop", "config.json"));
        }
    }

    [SetUp]
    public void SetUp() => _originalHome = Environment.GetEnvironmentVariable("HOME");

    [TearDown]
    public void TearDown() => Environment.SetEnvironmentVariable("HOME", _originalHome);
}
