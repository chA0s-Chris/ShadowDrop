// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

using ShadowDrop.Cli.Terminals;
using Spectre.Console;

/// <summary>
/// Selects a rich Spectre.Console reporter for interactive terminals and a deterministic plain-text reporter otherwise.
/// Progress output goes to standard output — downloads write file bytes to disk, never to stdout — so the rich-vs-plain
/// decision follows the capabilities of standard output.
/// </summary>
internal sealed class DownloadProgressReporterFactory(
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider,
    ITerminalCapabilityProvider capabilityProvider)
    : IDownloadProgressReporterFactory
{
    public DownloadProgressReporterFactory(TextWriter standardOut, TextWriter standardError, TimeProvider timeProvider)
        : this(standardOut, standardError, timeProvider, new TerminalCapabilityProvider()) { }

    private static IAnsiConsole CreateConsole(TextWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer)
        });

    public IDownloadProgressReporter Create()
    {
        if (DownloadProgressModeSelector.Select(capabilityProvider.DetectForStandardOutput()) == DownloadProgressMode.Rich)
        {
            return new SpectreDownloadProgressReporter(CreateConsole(standardOut), CreateConsole(standardError), timeProvider);
        }

        return new PlainTextDownloadProgressReporter(standardOut, standardError, timeProvider);
    }
}

/// <summary>
/// Always creates a deterministic plain-text reporter, used by tests to assert output without depending on terminal capabilities.
/// </summary>
internal sealed class PlainDownloadProgressReporterFactory(TextWriter standardOut, TextWriter standardError, TimeProvider timeProvider)
    : IDownloadProgressReporterFactory
{
    public IDownloadProgressReporter Create() => new PlainTextDownloadProgressReporter(standardOut, standardError, timeProvider);
}
