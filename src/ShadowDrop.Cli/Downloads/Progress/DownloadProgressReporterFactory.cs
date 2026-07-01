// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

using ShadowDrop.Cli.Terminals;
using Spectre.Console;

/// <summary>
/// Selects a rich Spectre.Console reporter for interactive terminals and a deterministic plain-text reporter otherwise.
/// </summary>
internal sealed class DownloadProgressReporterFactory(TextWriter standardError, TimeProvider timeProvider, ITerminalCapabilityProvider capabilityProvider)
    : IDownloadProgressReporterFactory
{
    public DownloadProgressReporterFactory(TextWriter standardError, TimeProvider timeProvider)
        : this(standardError, timeProvider, new TerminalCapabilityProvider()) { }

    public IDownloadProgressReporter Create()
    {
        if (DownloadProgressModeSelector.Select(capabilityProvider.DetectForStandardError()) == DownloadProgressMode.Rich)
        {
            var richConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(standardError)
            });
            return new SpectreDownloadProgressReporter(richConsole, timeProvider);
        }

        return new PlainTextDownloadProgressReporter(standardError, timeProvider);
    }
}

/// <summary>
/// Always creates a deterministic plain-text reporter, used by tests to assert output without depending on terminal capabilities.
/// </summary>
internal sealed class PlainDownloadProgressReporterFactory(TextWriter standardError, TimeProvider timeProvider) : IDownloadProgressReporterFactory
{
    public IDownloadProgressReporter Create() => new PlainTextDownloadProgressReporter(standardError, timeProvider);
}
