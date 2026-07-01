// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Terminals;

/// <summary>
/// Bundles the resolved <c>--no-banner</c> flag and per-stream terminal capabilities for a single invocation,
/// so command handlers can call the shared <see cref="CliBanner"/> formatter themselves, immediately before
/// their first real (non-error) output, without re-resolving global state or duplicating the corruption-avoidance
/// routing decision (stdout for human-readable output, stderr for byte-stream/JSON/parseable script output).
/// </summary>
internal sealed class CliBannerWriter(Boolean noBanner, TerminalCapabilities standardOutCapabilities, TerminalCapabilities standardErrorCapabilities)
{
    public Task WriteToStandardErrorAsync(TextWriter standardError, CancellationToken cancellationToken) =>
        noBanner ? Task.CompletedTask : CliBanner.WriteAsync(standardError, standardErrorCapabilities, cancellationToken);

    public Task WriteToStandardOutAsync(TextWriter standardOut, CancellationToken cancellationToken) =>
        noBanner ? Task.CompletedTask : CliBanner.WriteAsync(standardOut, standardOutCapabilities, cancellationToken);
}
