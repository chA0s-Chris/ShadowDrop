// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;

public sealed class InstallationGuidanceProviderTests
{
    [Test]
    public void GetInstallCommand_ShouldEscapePosixShellMetacharacters()
    {
        var provider = new InstallationGuidanceProvider(false, "/tmp/$(touch exploited)/it's `unsafe` \"here\"");

        provider.GetInstallCommand().Should()
                .EndWith("--install-dir '/tmp/$(touch exploited)/it'\\''s `unsafe` \"here\"'");
    }

    [Test]
    public void GetInstallCommand_ShouldEscapePowerShellMetacharacters()
    {
        var provider = new InstallationGuidanceProvider(true, "C:\\Tools\\$(Write-Host exploited)\\it's `unsafe` \"here\"");

        provider.GetInstallCommand().Should()
                .EndWith("-InstallDir 'C:\\Tools\\$(Write-Host exploited)\\it''s `unsafe` \"here\"'");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetInstallCommand_ShouldFallBackToDefaultUnixCommand_WhenExecutableDirectoryIsUnknown(String? executableDirectory)
    {
        var provider = new InstallationGuidanceProvider(false, executableDirectory);

        provider.GetInstallCommand().Should()
                .Be("curl -fsSL https://get.shadowdrop.net/install.sh | sh");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetInstallCommand_ShouldFallBackToDefaultWindowsCommand_WhenExecutableDirectoryIsUnknown(String? executableDirectory)
    {
        var provider = new InstallationGuidanceProvider(true, executableDirectory);

        provider.GetInstallCommand().Should()
                .Be("iwr -useb https://get.shadowdrop.net/install.ps1 | iex");
    }

    [Test]
    public void GetInstallCommand_ShouldPinUnixInstallerToExecutableDirectory()
    {
        var provider = new InstallationGuidanceProvider(false, "/home/alice/bin");

        provider.GetInstallCommand().Should()
                .Be("curl -fsSL https://get.shadowdrop.net/install.sh"
                    + " | sh -s -- --install-dir '/home/alice/bin'");
    }

    [Test]
    public void GetInstallCommand_ShouldPinWindowsInstallerToExecutableDirectory()
    {
        var provider = new InstallationGuidanceProvider(true, @"C:\Users\alice\Tools\ShadowDrop");

        // Plain "iwr | iex" cannot pass parameters, so the directory-pinned form uses the scriptblock invocation.
        provider.GetInstallCommand().Should()
                .Be("& ([scriptblock]::Create((iwr -useb https://get.shadowdrop.net/install.ps1)))"
                    + " -InstallDir 'C:\\Users\\alice\\Tools\\ShadowDrop'");
    }

    [Test]
    public void GetInstallCommand_ShouldQuoteDirectoriesContainingSpaces()
    {
        var unixProvider = new InstallationGuidanceProvider(false, "/home/alice/my tools/bin");
        var windowsProvider = new InstallationGuidanceProvider(true, @"C:\Program Files\ShadowDrop");

        unixProvider.GetInstallCommand().Should().EndWith("--install-dir '/home/alice/my tools/bin'");
        windowsProvider.GetInstallCommand().Should().EndWith("-InstallDir 'C:\\Program Files\\ShadowDrop'");
    }
}
