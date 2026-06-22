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
        HashSet<String> usedNames = new(StringComparer.Ordinal);
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
        var leaf = Path.GetFileName(fileName ?? String.Empty);
        if (String.IsNullOrWhiteSpace(leaf))
        {
            throw new QueueBuildException("A queued file has no usable name.");
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new String(leaf.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray()).Trim();

        if (String.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            throw new QueueBuildException("A queued file name cannot be sanitized into a safe output path.");
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
