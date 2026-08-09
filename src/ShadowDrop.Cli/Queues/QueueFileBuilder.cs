// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Queues;

using ShadowDrop.Cli.Files;
using ShadowDrop.Contracts;
using ShadowDrop.Queue;

/// <summary>
/// Builds a <see cref="QueueFile"/> from a normalized share manifest. Used by both the end-to-end upload
/// <c>--queue-out</c> path and the lower-level <c>queue create</c> command.
/// </summary>
internal static class QueueFileBuilder
{
    /// <summary>
    /// Builds a queue from the supplied share manifest.
    /// </summary>
    /// <param name="serverUrl">The base URL of the server hosting the share.</param>
    /// <param name="shareToken">The public share token used to download the share.</param>
    /// <param name="manifest">The share manifest describing the downloadable files.</param>
    /// <param name="credentials">
    /// Optional embedded credentials for a self-contained queue; <see langword="null"/> for a
    /// secret-free queue.
    /// </param>
    /// <param name="preparedDestinations">
    /// Destinations computed from the uploader's local paths before the upload, keyed by uploaded file id. Supplied
    /// by the end-to-end <c>upload --queue-out</c> workflow; <see langword="null"/> for <c>queue create</c>, which
    /// has no source paths and builds flat destinations from the manifest alone.
    /// </param>
    /// <returns>The assembled queue file.</returns>
    /// <exception cref="QueueBuildException">
    /// Thrown when the manifest is empty, an entry cannot produce a safe output path, or the manifest disagrees with
    /// the prepared destinations.
    /// </exception>
    public static QueueFile Build(Uri serverUrl,
                                  String shareToken,
                                  ShareManifestContract manifest,
                                  QueueCredentials? credentials,
                                  IReadOnlyDictionary<Guid, QueueDestination>? preparedDestinations = null)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(shareToken);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new QueueBuildException("The share manifest contains no files.");
        }

        // Compare case-insensitively so names differing only by case do not collide at write time on
        // case-insensitive file systems (Windows and many macOS setups).
        HashSet<String> usedNames = new(StringComparer.OrdinalIgnoreCase);
        List<QueueFileEntry> entries = [];
        List<String> outputPaths = [];
        var unmatchedPreparedFileIds = preparedDestinations is null
            ? []
            : new HashSet<Guid>(preparedDestinations.Keys);

        foreach (var file in manifest.Files)
        {
            var outputPath = preparedDestinations is null
                ? ResolveCollisionSafeName(file.FileName, usedNames)
                : ResolvePreparedDestination(file, preparedDestinations, unmatchedPreparedFileIds);
            outputPaths.Add(outputPath);
            entries.Add(new()
            {
                FileId = file.FileId,
                FileName = file.FileName,
                Length = file.Length,
                OutputPath = ToCanonicalOutputPath(outputPath, file.FileName),
                PlaintextSha256 = file.PlaintextSha256
            });
        }

        if (preparedDestinations is not null && unmatchedPreparedFileIds.Count > 0)
        {
            var omittedFileId = unmatchedPreparedFileIds.OrderBy(static fileId => fileId).First();
            throw new QueueBuildException($"The share manifest omitted uploaded file id '{omittedFileId}'.");
        }

        if (QueueOutputPath.TryFindConflict(outputPaths, out _, out var conflictError))
        {
            throw new QueueBuildException(conflictError);
        }

        return new()
        {
            ShadowDrop = FormatConstants.ShadowDropVersion,
            QueueVersion = FormatConstants.QueueVersion,
            ServerUrl = serverUrl.AbsoluteUri,
            ShareToken = shareToken,
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

    // The destination was already decided from local input, so a disagreement with what the share announces is a
    // remote consistency failure rather than something to silently recompute after the share exists.
    private static String ResolvePreparedDestination(ShareManifestFileContract file,
                                                     IReadOnlyDictionary<Guid, QueueDestination> preparedDestinations,
                                                     HashSet<Guid> unmatchedPreparedFileIds)
    {
        if (!Guid.TryParse(file.FileId, out var fileId) ||
            !preparedDestinations.TryGetValue(fileId, out var prepared) ||
            !unmatchedPreparedFileIds.Remove(fileId))
        {
            throw new QueueBuildException($"The share manifest announced file id '{file.FileId}', which was not part of this upload.");
        }

        var announcedName = Sanitize(file.FileName);
        if (!String.Equals(announcedName, prepared.ExpectedFileName, StringComparison.Ordinal))
        {
            throw new QueueBuildException(
                $"The share manifest announced '{file.FileName}' for a file queued as '{prepared.ExpectedFileName}'.");
        }

        return prepared.Path;
    }

    private static String Sanitize(String? fileName) =>
        SafeFileName.TrySanitize(fileName, out var safeFileName)
            ? safeFileName
            : throw new QueueBuildException("A queued file name cannot be sanitized into a safe output path.");

    // Version 2 treats an omitted outputPath as the file name, so the canonical form carries the value only when
    // sanitization, collision handling, or a nested destination made it differ.
    private static String? ToCanonicalOutputPath(String outputPath, String? fileName) =>
        String.Equals(outputPath, fileName, StringComparison.Ordinal) ? null : outputPath;
}

/// <summary>
/// Raised when a queue cannot be assembled from a share manifest.
/// </summary>
internal sealed class QueueBuildException : Exception
{
    public QueueBuildException(String message)
        : base(message) { }
}
