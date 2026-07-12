// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Persists the most recent update-check result so automatic checks can enforce their interval and serve a
/// fresh result without another release request.
/// </summary>
internal interface IUpdateCheckCache
{
    /// <summary>Reads the cached record, or <see langword="null"/> when it is missing or unreadable.</summary>
    UpdateCheckRecord? Read();

    /// <summary>Writes the record best-effort; a cache that cannot be written must never fail the check.</summary>
    void Write(UpdateCheckRecord record);
}
