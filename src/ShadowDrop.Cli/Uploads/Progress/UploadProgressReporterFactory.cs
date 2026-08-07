// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Terminals;
using Spectre.Console;

/// <summary>
/// Selects stderr-based upload progress reporting for rich terminals, deterministic plain output, or JSON suppression.
/// </summary>
internal sealed class UploadProgressReporterFactory
    : IUploadProgressReporterFactory
{
    private readonly ITerminalCapabilityProvider _capabilityProvider;
    private readonly TextWriter _standardError;
    private readonly TimeProvider _timeProvider;

    public UploadProgressReporterFactory(TextWriter standardError,
                                         TimeProvider timeProvider,
                                         ITerminalCapabilityProvider capabilityProvider)
    {
        _standardError = standardError;
        _timeProvider = timeProvider;
        _capabilityProvider = capabilityProvider;
    }

    private static IAnsiConsole CreateConsole(TextWriter writer) =>
        AnsiConsole.Create(new()
        {
            Out = new AnsiConsoleOutput(writer)
        });

    public IUploadProgressReporter Create(Boolean json)
    {
        if (json)
        {
            return NullUploadProgressReporter.Instance;
        }

        if (DownloadProgressModeSelector.Select(_capabilityProvider.DetectForStandardError()) == DownloadProgressMode.Rich)
        {
            var console = CreateConsole(_standardError);
            return new SpectreUploadProgressReporter(console, console, _timeProvider);
        }

        return new PlainTextUploadProgressReporter(_standardError, _timeProvider);
    }
}

/// <summary>
/// Always creates a deterministic plain-text upload reporter, used by tests to avoid terminal-dependent output.
/// </summary>
internal sealed class PlainUploadProgressReporterFactory : IUploadProgressReporterFactory
{
    private readonly TextWriter _standardError;
    private readonly TimeProvider _timeProvider;

    public PlainUploadProgressReporterFactory(TextWriter standardError,
                                              TimeProvider timeProvider)
    {
        _standardError = standardError;
        _timeProvider = timeProvider;
    }

    public IUploadProgressReporter Create(Boolean json) =>
        json ? NullUploadProgressReporter.Instance : new PlainTextUploadProgressReporter(_standardError, _timeProvider);
}
