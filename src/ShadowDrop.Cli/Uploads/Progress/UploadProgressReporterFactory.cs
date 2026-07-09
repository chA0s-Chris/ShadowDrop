// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Terminals;
using Spectre.Console;

/// <summary>
/// Selects stderr-based upload progress reporting for rich terminals, deterministic plain output, or JSON suppression.
/// </summary>
internal sealed class UploadProgressReporterFactory(
    TextWriter standardError,
    TimeProvider timeProvider,
    ITerminalCapabilityProvider capabilityProvider)
    : IUploadProgressReporterFactory
{
    private static IAnsiConsole CreateConsole(TextWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer)
        });

    public IUploadProgressReporter Create(Boolean json)
    {
        if (json)
        {
            return NullUploadProgressReporter.Instance;
        }

        if (DownloadProgressModeSelector.Select(capabilityProvider.DetectForStandardError()) == DownloadProgressMode.Rich)
        {
            var console = CreateConsole(standardError);
            return new SpectreUploadProgressReporter(console, console, timeProvider);
        }

        return new PlainTextUploadProgressReporter(standardError, timeProvider);
    }
}

/// <summary>
/// Always creates a deterministic plain-text upload reporter, used by tests to avoid terminal-dependent output.
/// </summary>
internal sealed class PlainUploadProgressReporterFactory(TextWriter standardError, TimeProvider timeProvider) : IUploadProgressReporterFactory
{
    public IUploadProgressReporter Create(Boolean json) =>
        json ? NullUploadProgressReporter.Instance : new PlainTextUploadProgressReporter(standardError, timeProvider);
}
