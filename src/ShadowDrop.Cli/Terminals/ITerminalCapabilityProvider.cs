// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Terminals;

/// <summary>
/// Detects terminal capabilities for the standard output and standard error streams, shared by download
/// progress reporting and banner rendering so both make the same rich-vs-plain decision.
/// </summary>
internal interface ITerminalCapabilityProvider
{
    TerminalCapabilities DetectForStandardError();
    TerminalCapabilities DetectForStandardOutput();
}
