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
internal sealed class DownloadProgressReporterFactory
    : IDownloadProgressReporterFactory
{
    private readonly ITerminalCapabilityProvider _capabilityProvider;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;

    public DownloadProgressReporterFactory(TextWriter standardOut,
                                           TextWriter standardError,
                                           TimeProvider timeProvider,
                                           ITerminalCapabilityProvider capabilityProvider)
    {
        _standardOut = standardOut;
        _standardError = standardError;
        _timeProvider = timeProvider;
        _capabilityProvider = capabilityProvider;
    }

    public DownloadProgressReporterFactory(TextWriter standardOut, TextWriter standardError, TimeProvider timeProvider)
        : this(standardOut, standardError, timeProvider, new TerminalCapabilityProvider()) { }

    private static IAnsiConsole CreateConsole(TextWriter writer) =>
        AnsiConsole.Create(new()
        {
            Out = new AnsiConsoleOutput(writer)
        });

    public IDownloadProgressReporter Create()
    {
        if (DownloadProgressModeSelector.Select(_capabilityProvider.DetectForStandardOutput()) == DownloadProgressMode.Rich)
        {
            return new SpectreDownloadProgressReporter(CreateConsole(_standardOut), CreateConsole(_standardError), _timeProvider);
        }

        return new PlainTextDownloadProgressReporter(_standardOut, _standardError, _timeProvider);
    }
}

/// <summary>
/// Always creates a deterministic plain-text reporter, used by tests to assert output without depending on terminal
/// capabilities.
/// </summary>
internal sealed class PlainDownloadProgressReporterFactory
    : IDownloadProgressReporterFactory
{
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;

    public PlainDownloadProgressReporterFactory(TextWriter standardOut,
                                                TextWriter standardError,
                                                TimeProvider timeProvider)
    {
        _standardOut = standardOut;
        _standardError = standardError;
        _timeProvider = timeProvider;
    }

    public IDownloadProgressReporter Create() => new PlainTextDownloadProgressReporter(_standardOut, _standardError, _timeProvider);
}
