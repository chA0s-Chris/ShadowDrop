// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Terminals;

/// <summary>
/// Describes the capabilities of a specific output stream (stdout or stderr) that determine whether
/// rich, colored, or interactive output can be used on that stream.
/// </summary>
internal readonly record struct TerminalCapabilities(Boolean IsRedirected, Boolean IsCiEnvironment, Boolean SupportsRichOutput);
