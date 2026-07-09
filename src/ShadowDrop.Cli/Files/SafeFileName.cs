// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Files;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Turns a server-announced file name into a portable, leaf-only name that is safe to use as a filesystem path
/// component. Every place where the server picks a name — queue building, single-file download destinations, and
/// the guided download workflow — routes through this helper so no server metadata can introduce path segments.
/// </summary>
internal static class SafeFileName
{
    // A fixed, OS-independent set so a name sanitized on one platform stays safe on another.
    // Combines the Windows-invalid characters (the strictest common set) with ASCII control characters.
    private static readonly HashSet<Char> PortableInvalidFileNameChars =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        ..Enumerable.Range(0, 32).Select(static value => (Char)value)
    ];

    // Windows reserved device names cannot be used as file names there (with or without an extension).
    private static readonly HashSet<String> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    /// <summary>
    /// Reduces <paramref name="fileName"/> to its leaf, replaces characters that are invalid on any supported
    /// platform, and rejects names that cannot become a usable file name.
    /// </summary>
    /// <param name="fileName">The server-announced name, which may contain directory components or be absent.</param>
    /// <param name="safeFileName">The sanitized leaf name when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a safe name could be derived; otherwise <see langword="false"/>.</returns>
    public static Boolean TrySanitize(String? fileName, [NotNullWhen(true)] out String? safeFileName)
    {
        safeFileName = null;

        // Strip directory components for both separators so the leaf is identical regardless of the host OS
        // (Path.GetFileName treats '\' as an ordinary character on non-Windows platforms).
        var raw = fileName ?? String.Empty;
        var separatorIndex = raw.LastIndexOfAny(['/', '\\']);
        var leaf = separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : raw;
        if (String.IsNullOrWhiteSpace(leaf))
        {
            return false;
        }

        var sanitized = new String(leaf.Select(static character => PortableInvalidFileNameChars.Contains(character) ? '_' : character).ToArray()).Trim();

        // Windows silently strips trailing dots and spaces, so normalize them here for stable cross-platform names.
        // This also collapses the relative-path names "." and ".." to an empty string, rejecting them below.
        sanitized = sanitized.TrimEnd('.', ' ');

        if (String.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        // Reserved device names are unusable as files on Windows; prefix them so the name stays writable everywhere.
        if (ReservedDeviceNames.Contains(Path.GetFileNameWithoutExtension(sanitized)))
        {
            sanitized = $"_{sanitized}";
        }

        safeFileName = sanitized;
        return true;
    }
}
