// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads.Progress;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads.Progress;

public sealed class DownloadProgressModeSelectorTests
{
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, true)]
    public void Select_ShouldReturnPlain_WhenRedirectedOrCiOrUnsupported(Boolean isErrorRedirected, Boolean isCiEnvironment, Boolean supportsRichOutput)
    {
        var capabilities = new TerminalCapabilities(isErrorRedirected, isCiEnvironment, supportsRichOutput);

        DownloadProgressModeSelector.Select(capabilities).Should().Be(DownloadProgressMode.Plain);
    }

    [Test]
    public void Select_ShouldReturnRich_ForInteractiveTerminalSupportingRichOutput()
    {
        var capabilities = new TerminalCapabilities(IsErrorRedirected: false, IsCiEnvironment: false, SupportsRichOutput: true);

        DownloadProgressModeSelector.Select(capabilities).Should().Be(DownloadProgressMode.Rich);
    }
}
