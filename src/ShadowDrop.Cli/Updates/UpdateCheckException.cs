// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Raised when the latest-release lookup fails; the message is user-facing and actionable so the explicit
/// <c>update</c> command can print it verbatim.
/// </summary>
internal sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(String message)
        : base(message) { }

    public UpdateCheckException(String message, Exception innerException)
        : base(message, innerException) { }
}
