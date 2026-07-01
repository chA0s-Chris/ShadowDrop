// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Terminals;
using Spectre.Console;

/// <summary>
/// Renders the decorated two-line "ShadowDrop v&lt;version&gt;" banner shown at the start of help and command
/// output. This is the single formatter shared by both surfaces so they cannot drift; callers decide the
/// destination writer and whether a banner is wanted at all (see <see cref="CliBannerWriter"/>).
/// </summary>
internal static class CliBanner
{
    private const String BorderColor = "dodgerblue1";
    private const String HighlightColor = "cyan1";

    public static Task WriteAsync(TextWriter writer, TerminalCapabilities capabilities, CancellationToken cancellationToken) =>
        WriteAsync(writer, capabilities, CliVersion.Current, cancellationToken);

    internal static (String Top, String Bottom) BuildPlainLines(String version)
    {
        var label = $"ShadowDrop v{version}";
        var hyphens = new String('-', HyphenCount(version));
        var top = $@".--// {label} \\--.";
        var bottom = $"`--/{hyphens}\\--´";
        return (top, bottom);
    }

    internal static async Task WriteAsync(TextWriter writer, TerminalCapabilities capabilities, String version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!capabilities.SupportsRichOutput)
        {
            var (top, bottom) = BuildPlainLines(version);
            await writer.WriteLineAsync(top.AsMemory(), cancellationToken);
            await writer.WriteLineAsync(bottom.AsMemory(), cancellationToken);
            await writer.WriteLineAsync(ReadOnlyMemory<Char>.Empty, cancellationToken);
            return;
        }

        // Capability selection already happened above; force Ansi/color support instead of letting Spectre
        // re-detect it from the destination writer, which would misdetect a StringWriter or piped stream as plain.
        var console = AnsiConsole.Create(new()
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(writer)
        });

        var escapedVersion = Markup.Escape(version);
        var hyphens = new String('-', HyphenCount(version));

        console.Markup($"[{BorderColor}].--[/]");
        console.Markup($"[{HighlightColor}]//[/]");
        console.Markup(" [bold white]ShadowDrop [/]");
        console.Markup($"[bold {BorderColor}]v[/]");
        console.Markup($"[bold {HighlightColor}]{escapedVersion}[/]");
        console.Markup($" [{HighlightColor}]\\\\[/]");
        console.MarkupLine($"[{BorderColor}]--.[/]");

        console.Markup($"[{BorderColor}]`--[/]");
        console.Markup($"[{HighlightColor}]/[/]");
        console.Markup($"[{BorderColor}]{hyphens}[/]");
        console.Markup($"[{HighlightColor}]\\[/]");
        console.MarkupLine($"[{BorderColor}]--´[/]");

        console.WriteLine();
    }

    // Keeps both rendered lines the same width. The top wraps the label in 12 characters of decoration
    // (".--// " before, " \\--." after) while the bottom wraps the hyphen run in 8 ("`--/" before, "\--´"
    // after), so matching widths requires hyphens = label.Length + 4 (the 12 - 8 decoration difference).
    private static Int32 HyphenCount(String version) => $"ShadowDrop v{version}".Length + 4;
}
