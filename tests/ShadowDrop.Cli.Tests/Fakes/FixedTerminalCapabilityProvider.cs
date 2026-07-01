// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Cli.Terminals;

/// <summary>
/// Returns the same, test-supplied <see cref="TerminalCapabilities"/> for both streams, so banner and download
/// progress rendering can be asserted deterministically without depending on the real process terminal.
/// </summary>
internal sealed class FixedTerminalCapabilityProvider(TerminalCapabilities capabilities) : ITerminalCapabilityProvider
{
    public static FixedTerminalCapabilityProvider Plain { get; } = new(new(IsRedirected: true, IsCiEnvironment: false, SupportsRichOutput: false));

    public TerminalCapabilities DetectForStandardError() => capabilities;

    public TerminalCapabilities DetectForStandardOutput() => capabilities;
}
