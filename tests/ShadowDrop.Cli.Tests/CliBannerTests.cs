// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Terminals;

public sealed class CliBannerTests
{
    private static readonly TerminalCapabilities Plain = new(IsRedirected: true, IsCiEnvironment: false, SupportsRichOutput: false);
    private static readonly TerminalCapabilities Rich = new(IsRedirected: false, IsCiEnvironment: false, SupportsRichOutput: true);

    [Test]
    public void BuildPlainLines_ShouldAddOneMoreHyphen_WhenVersionIsOneCharacterLonger()
    {
        var (_, shortBottom) = CliBanner.BuildPlainLines("x.x.x");
        var (_, longBottom) = CliBanner.BuildPlainLines("1.0.21");

        // "1.0.21" is one character longer than the "x.x.x" placeholder, so its hyphen run grows by exactly one.
        (longBottom.Length - shortBottom.Length).Should().Be(1);
    }

    [Test]
    public void BuildPlainLines_ShouldKeepBothLinesTheSameWidth_ForAnyVersionLength()
    {
        foreach (var version in new[]
                 {
                     "0.0.1",
                     "1.0.21",
                     "10.20.300-preview.1"
                 })
        {
            var (top, bottom) = CliBanner.BuildPlainLines(version);

            top.Length.Should().Be(bottom.Length, $"top and bottom lines must match in width for version '{version}'");
        }
    }

    [Test]
    public void BuildPlainLines_ShouldMatchDocumentedShape_ForPlaceholderVersion()
    {
        var (top, bottom) = CliBanner.BuildPlainLines("x.x.x");

        top.Should().Be(".--// ShadowDrop vx.x.x \\\\--.");
        bottom.Should().Be($"`--/{new String('-', 21)}\\--´");
        top.Length.Should().Be(bottom.Length);
    }

    [Test]
    public async Task WriteAsync_ShouldNotEmitAnsiEscapes_WhenRichOutputIsUnsupported()
    {
        var writer = new StringWriter();

        await CliBanner.WriteAsync(writer, Plain, "1.2.3", CancellationToken.None);

        writer.ToString().Should().NotContain("[");
    }

    [Test]
    public async Task WriteAsync_ShouldRenderColoredSegments_WhenRichOutputIsSupported()
    {
        var writer = new StringWriter();

        await CliBanner.WriteAsync(writer, Rich, "1.2.3", CancellationToken.None);

        var output = writer.ToString();
        output.Should().Contain("ShadowDrop").And.Contain("1.2.3");
        // Rich rendering emits ANSI escape sequences (color codes) around the banner segments, unlike plain mode.
        output.Should().Contain("[");
    }

    [Test]
    public async Task WriteAsync_ShouldRenderPlainTwoLineShape_WhenRichOutputIsUnsupported()
    {
        var writer = new StringWriter();

        await CliBanner.WriteAsync(writer, Plain, "1.2.3", CancellationToken.None);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var (expectedTop, expectedBottom) = CliBanner.BuildPlainLines("1.2.3");
        lines.Should().Equal(expectedTop, expectedBottom);
    }

    [Test]
    public async Task WriteAsync_ShouldUseCurrentCliVersion_WhenNoVersionIsSpecified()
    {
        var writer = new StringWriter();

        await CliBanner.WriteAsync(writer, Plain, CancellationToken.None);

        var (expectedTop, _) = CliBanner.BuildPlainLines(CliVersion.Current);
        writer.ToString().Should().Contain(expectedTop);
    }
}
