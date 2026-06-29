// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// The kind of download progress output to emit.
/// </summary>
internal enum DownloadProgressMode
{
    /// <summary>
    /// Deterministic plain-text lifecycle lines for redirected stderr, CI, and other non-interactive environments.
    /// </summary>
    Plain,

    /// <summary>
    /// Rich Spectre.Console live progress for interactive terminals.
    /// </summary>
    Rich
}

/// <summary>
/// Describes the terminal capabilities that determine the download progress output mode.
/// </summary>
internal readonly record struct TerminalCapabilities(Boolean IsErrorRedirected, Boolean IsCiEnvironment, Boolean SupportsRichOutput);

/// <summary>
/// Selects the download progress output mode from terminal capabilities.
/// </summary>
internal static class DownloadProgressModeSelector
{
    /// <summary>
    /// Returns <see cref="DownloadProgressMode.Rich"/> only for an interactive terminal that supports rich output and is not redirected or running in CI.
    /// </summary>
    public static DownloadProgressMode Select(TerminalCapabilities capabilities) =>
        capabilities is { IsErrorRedirected: false, IsCiEnvironment: false, SupportsRichOutput: true }
            ? DownloadProgressMode.Rich
            : DownloadProgressMode.Plain;
}
