// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Terminals;

/// <summary>
/// Detects terminal capabilities from the real process environment (<see cref="Console"/> redirection state
/// and the <c>CI</c>/<c>TERM</c> environment variables).
/// </summary>
internal sealed class TerminalCapabilityProvider : ITerminalCapabilityProvider
{
    private static TerminalCapabilities Detect(Boolean isRedirected)
    {
        var isCi = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
        var isDumbTerminal = String.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
        return new(isRedirected, isCi, SupportsRichOutput: !isRedirected && !isCi && !isDumbTerminal);
    }

    public TerminalCapabilities DetectForStandardError() => Detect(Console.IsErrorRedirected);
    public TerminalCapabilities DetectForStandardOutput() => Detect(Console.IsOutputRedirected);
}
