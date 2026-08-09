// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Resolves and validates the portable relative destinations carried by a queue file.
/// </summary>
/// <remarks>
/// Queue destinations are validated independently of the host operating system so a queue written on one platform
/// behaves identically on another: <c>/</c> is the only directory separator, <c>\</c> is always rejected rather
/// than reinterpreted, and every segment must be usable as a file name on all supported platforms.
/// </remarks>
public static class QueueOutputPath
{
    /// <summary>
    /// The only directory separator a queue destination may use.
    /// </summary>
    public const Char DirectorySeparator = '/';
    private const String PartialFileSuffix = ".shadowdrop-partial";

    // A fixed, OS-independent set so a path accepted on one platform stays writable on another. Combines the
    // Windows-invalid characters (the strictest common set) with ASCII control characters. The directory
    // separators are handled structurally instead, and ':' being invalid also rejects drive-qualified segments.
    private static readonly HashSet<Char> PortableInvalidSegmentChars =
    [
        '<', '>', ':', '"', '|', '?', '*',
        .. Enumerable.Range(0, 32).Select(static value => (Char)value)
    ];
    private const String ResumeMarkerSuffix = ".json";

    private static readonly HashSet<String> WindowsReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM¹",
        "COM²",
        "COM³",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT¹",
        "LPT²",
        "LPT³",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    /// <summary>
    /// Resolves the effective destination of a queue entry, which is its explicit output path when present and its
    /// file name otherwise.
    /// </summary>
    /// <param name="entry">The queue file entry.</param>
    /// <returns>The effective destination, or <see langword="null"/> when the entry carries neither value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is <see langword="null"/>.</exception>
    public static String? Resolve(QueueFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.OutputPath ?? entry.FileName;
    }

    /// <summary>
    /// Splits a validated destination into its path segments.
    /// </summary>
    /// <param name="path">The destination to split.</param>
    /// <returns>The path segments, in order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<String> SplitSegments(String path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split(DirectorySeparator);
    }

    /// <summary>
    /// Finds the first destination that duplicates another, collides with it because one is a directory ancestor
    /// of the other, or overlaps a resume-artifact path reserved by another destination. Comparison is
    /// case-insensitive so a queue cannot collide at write time on Windows or on common macOS file systems.
    /// </summary>
    /// <param name="paths">The effective destinations to inspect, in entry order.</param>
    /// <param name="index">The index of the conflicting destination when this method returns <see langword="true"/>.</param>
    /// <param name="error">The conflict description when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a conflict was found; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is <see langword="null"/>.</exception>
    public static Boolean TryFindConflict(IReadOnlyList<String> paths, out Int32 index, [NotNullWhen(true)] out String? error)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Every proper prefix of a destination is a directory it needs, so a destination that equals another's
        // prefix is a file/directory conflict ('docs' cannot be both a file and the parent of 'docs/report.txt').
        Dictionary<String, Int32> directoryOwners = new(StringComparer.OrdinalIgnoreCase);
        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var segments = SplitSegments(paths[pathIndex]);
            for (var length = 1; length < segments.Count; length++)
            {
                directoryOwners.TryAdd(String.Join(DirectorySeparator, segments.Take(length)), pathIndex);
            }
        }

        Dictionary<String, Int32> fileOwners = new(StringComparer.OrdinalIgnoreCase);
        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            if (!fileOwners.TryAdd(path, pathIndex))
            {
                index = pathIndex;
                error = $"The output path '{path}' is used by more than one file entry.";
                return true;
            }

            if (directoryOwners.TryGetValue(path, out var owner) && owner != pathIndex)
            {
                index = pathIndex;
                error = $"The output path '{path}' is also used as a directory by another file entry.";
                return true;
            }
        }

        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var (partialPath, markerPath) = ResolveResumePaths(paths[pathIndex]);
            if (TryFindResumeArtifactConflict(paths,
                                              pathIndex,
                                              partialPath,
                                              fileOwners,
                                              directoryOwners,
                                              out index,
                                              out error)
                || TryFindResumeArtifactConflict(paths,
                                                 pathIndex,
                                                 markerPath,
                                                 fileOwners,
                                                 directoryOwners,
                                                 out index,
                                                 out error))
            {
                return true;
            }
        }

        index = -1;
        error = null;
        return false;
    }

    /// <summary>
    /// Validates a portable relative destination.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="valueName">The name of the validated value, used to compose the error message.</param>
    /// <param name="allowDirectorySeparators">
    /// <see langword="true"/> for an explicit output path, which may describe a nested destination;
    /// <see langword="false"/> for a value that must be a single path segment, such as a server-announced file name.
    /// </param>
    /// <param name="error">The validation error when this method returns <see langword="false"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the value is a valid portable relative destination; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static Boolean TryValidate([NotNullWhen(true)] String? value,
                                      String valueName,
                                      Boolean allowDirectorySeparators,
                                      [NotNullWhen(false)] out String? error)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            error = $"The {valueName} value is required.";
            return false;
        }

        if (value.Contains('\\'))
        {
            error = $"The {valueName} value must use '{DirectorySeparator}' as its directory separator.";
            return false;
        }

        if (value.StartsWith(DirectorySeparator))
        {
            error = $"The {valueName} value must be a relative path.";
            return false;
        }

        var segments = SplitSegments(value);
        if (!allowDirectorySeparators && segments.Count > 1)
        {
            error = $"The {valueName} value must not contain directory separators; carry a nested destination in outputPath instead.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (String.IsNullOrWhiteSpace(segment))
            {
                error = $"The {valueName} value must not contain empty path segments.";
                return false;
            }

            if (segment is "." or "..")
            {
                error = $"The {valueName} value must not contain '.' or '..' path segments.";
                return false;
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                error = $"The {valueName} value must not contain path segments ending in a dot or space.";
                return false;
            }

            if (segment.Any(PortableInvalidSegmentChars.Contains))
            {
                error = $"The {valueName} value must not contain the characters <>:\"|?* or control characters.";
                return false;
            }

            if (IsWindowsReservedDeviceName(segment))
            {
                error = $"The {valueName} value must not contain Windows reserved device-name segments.";
                return false;
            }
        }

        error = null;
        return true;
    }

    internal static Boolean IsWindowsReservedDeviceName(String segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var extensionSeparatorIndex = segment.IndexOf('.');
        var deviceName = extensionSeparatorIndex < 0 ? segment : segment[..extensionSeparatorIndex];
        return WindowsReservedDeviceNames.Contains(deviceName);
    }

    internal static (String PartialPath, String MarkerPath) ResolveResumePaths(String outputPath)
    {
        ArgumentNullException.ThrowIfNull(outputPath);

        var partialPath = $"{outputPath}{PartialFileSuffix}";
        return (partialPath, $"{partialPath}{ResumeMarkerSuffix}");
    }

    private static String CreateResumeArtifactConflictError(String outputPath, String artifactPath) =>
        $"The output path '{outputPath}' conflicts with the reserved download resume path '{artifactPath}' of another file entry.";

    private static Boolean TryFindResumeArtifactConflict(IReadOnlyList<String> paths,
                                                         Int32 artifactOwner,
                                                         String artifactPath,
                                                         IReadOnlyDictionary<String, Int32> fileOwners,
                                                         IReadOnlyDictionary<String, Int32> directoryOwners,
                                                         out Int32 index,
                                                         [NotNullWhen(true)] out String? error)
    {
        if (fileOwners.TryGetValue(artifactPath, out var fileOwner) && fileOwner != artifactOwner)
        {
            index = Math.Max(artifactOwner, fileOwner);
            error = CreateResumeArtifactConflictError(paths[index], artifactPath);
            return true;
        }

        if (directoryOwners.TryGetValue(artifactPath, out var directoryOwner) && directoryOwner != artifactOwner)
        {
            index = Math.Max(artifactOwner, directoryOwner);
            error = CreateResumeArtifactConflictError(paths[index], artifactPath);
            return true;
        }

        var segments = SplitSegments(artifactPath);
        for (var length = 1; length < segments.Count; length++)
        {
            var ancestorPath = String.Join(DirectorySeparator, segments.Take(length));
            if (fileOwners.TryGetValue(ancestorPath, out var ancestorOwner) && ancestorOwner != artifactOwner)
            {
                index = Math.Max(artifactOwner, ancestorOwner);
                error = CreateResumeArtifactConflictError(paths[index], artifactPath);
                return true;
            }
        }

        index = -1;
        error = null;
        return false;
    }
}
