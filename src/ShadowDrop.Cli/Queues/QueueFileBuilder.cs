// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Queues;

using ShadowDrop.Contracts;
using ShadowDrop.Queue;

/// <summary>
/// Builds a <see cref="QueueFile"/> from a normalized share manifest. Used by both the end-to-end upload
/// <c>--queue-out</c> path and the lower-level <c>queue create</c> command.
/// </summary>
internal static class QueueFileBuilder
{
    // A fixed, OS-independent set so a queue generated on one platform stays safe to download on another.
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
    /// Builds a queue from the supplied share manifest.
    /// </summary>
    /// <param name="serverUrl">The base URL of the server hosting the share.</param>
    /// <param name="shareToken">The public share token used to download the share.</param>
    /// <param name="manifest">The share manifest describing the downloadable files.</param>
    /// <param name="credentials">Optional embedded credentials for a self-contained queue; <see langword="null"/> for a secret-free queue.</param>
    /// <returns>The assembled queue file.</returns>
    /// <exception cref="QueueBuildException">Thrown when the manifest is empty or an entry cannot produce a safe output path.</exception>
    public static QueueFile Build(Uri serverUrl, String shareToken, ShareManifestContract manifest, QueueCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(shareToken);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new QueueBuildException("The share manifest contains no files.");
        }

        var serverUrlText = serverUrl.AbsoluteUri;

        // Compare case-insensitively so names differing only by case do not collide at write time on
        // case-insensitive file systems (Windows and many macOS setups).
        HashSet<String> usedNames = new(StringComparer.OrdinalIgnoreCase);
        List<QueueFileEntry> entries = [];

        foreach (var file in manifest.Files)
        {
            var outputPath = ResolveCollisionSafeName(file.FileName, usedNames);
            entries.Add(new()
            {
                ServerUrl = serverUrlText,
                ShareToken = shareToken,
                FileId = file.FileId,
                FileName = file.FileName,
                Length = file.Length,
                OutputPath = outputPath,
                PlaintextSha256 = file.PlaintextSha256
            });
        }

        return new()
        {
            ShadowDrop = FormatConstants.ShadowDropVersion,
            QueueVersion = FormatConstants.QueueVersion,
            Credentials = credentials,
            Files = entries
        };
    }

    private static String ResolveCollisionSafeName(String? fileName, HashSet<String> usedNames)
    {
        var safeName = Sanitize(fileName);
        var candidate = safeName;
        var extension = Path.GetExtension(safeName);
        var stem = safeName[..^extension.Length];

        for (var counter = 2; !usedNames.Add(candidate); counter++)
        {
            candidate = $"{stem} ({counter}){extension}";
        }

        return candidate;
    }

    private static String Sanitize(String? fileName)
    {
        // Strip directory components for both separators so the leaf is identical regardless of the host OS
        // (Path.GetFileName treats '\' as an ordinary character on non-Windows platforms).
        var raw = fileName ?? String.Empty;
        var separatorIndex = raw.LastIndexOfAny(['/', '\\']);
        var leaf = separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : raw;
        if (String.IsNullOrWhiteSpace(leaf))
        {
            throw new QueueBuildException("A queued file has no usable name.");
        }

        var sanitized = new String(leaf.Select(static character => PortableInvalidFileNameChars.Contains(character) ? '_' : character).ToArray()).Trim();

        // Windows silently strips trailing dots and spaces, so normalize them here for stable cross-platform names.
        sanitized = sanitized.TrimEnd('.', ' ');

        if (String.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            throw new QueueBuildException("A queued file name cannot be sanitized into a safe output path.");
        }

        // Reserved device names are unusable as files on Windows; prefix them so the queue stays writable everywhere.
        if (ReservedDeviceNames.Contains(Path.GetFileNameWithoutExtension(sanitized)))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }
}

/// <summary>
/// Raised when a queue cannot be assembled from a share manifest.
/// </summary>
internal sealed class QueueBuildException : Exception
{
    public QueueBuildException(String message)
        : base(message) { }
}
