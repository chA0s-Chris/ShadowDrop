// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Tests.Fakes;

public sealed class UpdateCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");

    [Test]
    public async Task ExecuteAsync_ShouldPrintUnixInstallCommand_WhenUpdateIsAvailable()
    {
        var cache = new InMemoryUpdateCheckCache();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = CreateHandler(new("v1.5.0"), cache, standardOut, standardError, "1.4.0",
                                    executableDirectory: "/home/alice/bin");

        var exitCode = await handler.ExecuteAsync(CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "installed-version:1.4.0",
            "latest-version:1.5.0",
            "update-status:update-available",
            "update-command:curl -fsSL https://get.shadowdrop.net/install.sh"
            + " | sh -s -- --install-dir '/home/alice/bin'");
    }

    [Test]
    public async Task ExecuteAsync_ShouldPrintWindowsInstallCommand_WhenUpdateIsAvailable()
    {
        var standardOut = new StringWriter();
        var handler = CreateHandler(new("v1.5.0"), new(), standardOut, new(),
                                    "1.4.0", true, @"C:\Users\alice\Tools\ShadowDrop");

        var exitCode = await handler.ExecuteAsync(CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Should()
                   .Contain("update-command:& ([scriptblock]::Create((iwr -useb"
                            + " https://get.shadowdrop.net/install.ps1)))"
                            + " -InstallDir 'C:\\Users\\alice\\Tools\\ShadowDrop'");
    }

    [Test]
    public async Task ExecuteAsync_ShouldRefreshCache_AfterSuccessfulCheck()
    {
        var cache = new InMemoryUpdateCheckCache();
        var handler = CreateHandler(new("v1.5.0"), cache, new(), new(), "1.4.0");

        await handler.ExecuteAsync(CancellationToken.None);

        cache.Record.Should().Be(new UpdateCheckRecord(Now, "1.5.0"));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReportUpdate_WhenInstalledPrereleaseIsBelowLatestStable()
    {
        var standardOut = new StringWriter();
        var handler = CreateHandler(new("v1.5.0"), new(), standardOut, new(),
                                    "1.5.0-preview.2");

        var exitCode = await handler.ExecuteAsync(CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Should().Contain("update-status:update-available");
    }

    [TestCase("1.5.0", TestName = "installed equals latest")]
    [TestCase("1.6.0", TestName = "installed is newer than latest")]
    public async Task ExecuteAsync_ShouldReportUpToDate_WithoutInstallCommand(String installedVersion)
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = CreateHandler(new("v1.5.0"), new(), standardOut, standardError,
                                    installedVersion);

        var exitCode = await handler.ExecuteAsync(CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            $"installed-version:{installedVersion}",
            "latest-version:1.5.0",
            "update-status:up-to-date");
    }

    [Test]
    public async Task ExecuteAsync_ShouldWriteDiagnosticAndFail_WhenReleaseCheckFails()
    {
        var cache = new InMemoryUpdateCheckCache();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = CreateHandler(new(new UpdateCheckException("The update check timed out after 5 seconds.")),
                                    cache, standardOut, standardError, "1.4.0");

        var exitCode = await handler.ExecuteAsync(CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be("The update check timed out after 5 seconds.");
        cache.WriteCount.Should().Be(0);
    }

    private static UpdateCommandHandler CreateHandler(StubUpdateReleaseClient releaseClient,
                                                      InMemoryUpdateCheckCache cache,
                                                      StringWriter standardOut,
                                                      StringWriter standardError,
                                                      String installedVersion,
                                                      Boolean isWindows = false,
                                                      String? executableDirectory = null) =>
        new(FakeUpdateServices.Create(releaseClient, cache, isWindows, executableDirectory: executableDirectory),
            standardOut,
            standardError,
            new ManualTimeProvider(Now),
            installedVersion);
}
