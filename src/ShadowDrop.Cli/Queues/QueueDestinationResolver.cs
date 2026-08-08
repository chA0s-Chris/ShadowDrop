// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Queues;

using ShadowDrop.Cli.Files;
using ShadowDrop.Queue;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// How <c>upload --queue-out</c> derives a queue destination for each selected file.
/// </summary>
internal enum QueueDestinationMode
{
    /// <summary>
    /// Preserve each file's path relative to the effective input root. Files outside that root are rejected.
    /// </summary>
    Preserve,

    /// <summary>
    /// Discard source directories and use only each file's leaf name, which allows inputs from unrelated locations.
    /// </summary>
    Flatten
}

/// <summary>
/// One prepared queue destination.
/// </summary>
/// <param name="Path">The portable, collision-resolved relative destination written to the queue.</param>
/// <param name="ExpectedFileName">
/// The sanitized leaf before collision resolution, which is what the share manifest is expected to announce for
/// this file. Carried so queue building can detect a remote inconsistency instead of silently recomputing.
/// </param>
internal sealed record QueueDestination(String Path, String ExpectedFileName);

/// <summary>
/// Computes the portable queue destination of every selected upload file before any upload or share-creation
/// request runs, so a root, sanitization, duplicate, or hierarchy failure is reported without remote side effects.
/// </summary>
/// <remarks>
/// Both modes compare canonical destinations case-insensitively so a plan cannot collide at write time on Windows
/// or on common macOS file systems, and both emit <c>/</c>-separated relative paths regardless of the host.
/// </remarks>
internal static class QueueDestinationResolver
{
    /// <summary>
    /// Resolves the queue destination of every selected file.
    /// </summary>
    /// <param name="files">The files selected for upload, in command order.</param>
    /// <param name="displayNameOverrides">Recipient-facing display names keyed by <see cref="FileSystemInfo.FullName"/>.</param>
    /// <param name="mode">Whether to preserve relative directories or flatten to leaf names.</param>
    /// <param name="inputRoot">
    /// The absolute directory that <see cref="QueueDestinationMode.Preserve"/> resolves paths against. Ignored in
    /// <see cref="QueueDestinationMode.Flatten"/> mode, where no source root is derived or validated.
    /// </param>
    /// <param name="destinations">The destinations keyed by <see cref="FileSystemInfo.FullName"/> when this method returns <see langword="true"/>.</param>
    /// <param name="error">The failure description when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when every file produced a safe, unique destination; otherwise <see langword="false"/>.</returns>
    public static Boolean TryResolve(IReadOnlyList<FileInfo> files,
                                     IReadOnlyDictionary<String, String> displayNameOverrides,
                                     QueueDestinationMode mode,
                                     String inputRoot,
                                     [NotNullWhen(true)] out IReadOnlyDictionary<String, QueueDestination>? destinations,
                                     [NotNullWhen(false)] out String? error)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(displayNameOverrides);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRoot);

        destinations = null;

        // Both sides of every containment and relative-path decision go through the same normalization, and links
        // are deliberately not resolved: the comparison stays predictable and testable without touching the disk.
        var normalizedRoot = Path.GetFullPath(inputRoot);
        HashSet<String> usedDestinations = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<String, QueueDestination> resolved = new(StringComparer.Ordinal);
        List<String> plannedFiles = [];
        List<String> plannedDestinations = [];

        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file.FullName);
            var displayName = displayNameOverrides.GetValueOrDefault(fullPath);

            if (!TryResolveSegments(file, fullPath, displayName, mode, normalizedRoot, out var segments, out error))
            {
                return false;
            }

            var destinationPath = ResolveCollisionSafeDestination(segments, usedDestinations);

            // Defense in depth: every segment is already sanitized, so this only catches a rule the sanitizer and
            // the queue format could ever disagree on.
            if (!QueueOutputPath.TryValidate(destinationPath, "destination", true, out var validationError))
            {
                error = $"The queue destination for '{fullPath}' is invalid. {validationError}";
                return false;
            }

            if (!resolved.TryAdd(fullPath, new(destinationPath, segments[^1])))
            {
                error = $"The file '{fullPath}' was selected more than once.";
                return false;
            }

            plannedFiles.Add(fullPath);
            plannedDestinations.Add(destinationPath);
        }

        // Exact collisions were already resolved by suffixing; a destination that another entry needs as a directory
        // cannot be resolved that way and is reported instead.
        if (QueueOutputPath.TryFindConflict(plannedDestinations, out var conflictIndex, out var conflictError))
        {
            error = $"{conflictError} It was derived from '{plannedFiles[conflictIndex]}'; rename the file or re-run with --flatten.";
            return false;
        }

        destinations = resolved;
        error = null;
        return true;
    }

    private static String ResolveCollisionSafeDestination(IReadOnlyList<String> segments, HashSet<String> usedDestinations)
    {
        var directory = segments.Count > 1
            ? String.Join(QueueOutputPath.DirectorySeparator, segments.Take(segments.Count - 1)) + QueueOutputPath.DirectorySeparator
            : String.Empty;
        var leaf = segments[^1];
        var extension = Path.GetExtension(leaf);
        var stem = leaf[..^extension.Length];

        var candidate = $"{directory}{leaf}";
        for (var counter = 2; !usedDestinations.Add(candidate); counter++)
        {
            candidate = $"{directory}{stem} ({counter}){extension}";
        }

        return candidate;
    }

    private static Boolean TryResolveSegments(FileInfo file,
                                              String fullPath,
                                              String? displayName,
                                              QueueDestinationMode mode,
                                              String normalizedRoot,
                                              [NotNullWhen(true)] out IReadOnlyList<String>? segments,
                                              [NotNullWhen(false)] out String? error)
    {
        segments = null;

        if (mode == QueueDestinationMode.Flatten)
        {
            if (!TrySanitizeSegment(displayName ?? file.Name, fullPath, out var leaf, out error))
            {
                return false;
            }

            segments = [leaf];
            return true;
        }

        if (!IsInsideRoot(fullPath, normalizedRoot))
        {
            error = $"The file '{fullPath}' is outside the input root '{normalizedRoot}'. "
                    + "Pass --input-root <directory> to choose a root that contains it, or --flatten to drop source directories.";
            return false;
        }

        var relativePath = Path.GetRelativePath(normalizedRoot, fullPath);
        List<String> resolvedSegments = [];
        var rawSegments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                                             StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < rawSegments.Length; index++)
        {
            // The recipient-facing display name replaces the destination leaf but keeps the derived directory.
            var isLeaf = index == (rawSegments.Length - 1);
            var raw = isLeaf ? displayName ?? rawSegments[index] : rawSegments[index];
            if (!TrySanitizeSegment(raw, fullPath, out var segment, out error))
            {
                return false;
            }

            resolvedSegments.Add(segment);
        }

        if (resolvedSegments.Count == 0)
        {
            error = $"The file '{fullPath}' does not produce a queue destination relative to '{normalizedRoot}'.";
            return false;
        }

        segments = resolvedSegments;
        error = null;
        return true;
    }

    private static Boolean TrySanitizeSegment(String? raw,
                                              String fullPath,
                                              [NotNullWhen(true)] out String? segment,
                                              [NotNullWhen(false)] out String? error)
    {
        // Every segment goes through the same portable filename rules the server-announced names use, so a
        // preserved directory cannot introduce a name that is unusable on another platform.
        if (!SafeFileName.TrySanitize(raw, out var sanitized))
        {
            segment = null;
            error = $"The path component '{raw}' of '{fullPath}' cannot be sanitized into a safe queue destination.";
            return false;
        }

        segment = sanitized;
        error = null;
        return true;
    }

    private static Boolean IsInsideRoot(String path, String root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, comparison);
    }
}
