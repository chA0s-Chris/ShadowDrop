// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Cli.Files;
using ShadowDrop.Contracts;

/// <summary>
/// Resolves where a single-file download is written. Without <c>--out</c> the file lands in the current directory
/// under its announced name; with <c>--out</c> the user's value decides between a directory and an explicit file
/// path. Only the server-announced name is sanitized — the user's own <c>--out</c> value is honored verbatim,
/// including absolute paths and relative traversal.
/// </summary>
internal static class DownloadDestinationResolver
{
    private const String FallbackFileName = "download.bin";

    /// <summary>
    /// Resolves the absolute destination path for a single-file download.
    /// </summary>
    /// <param name="outOption">The raw <c>--out</c> value, or <see langword="null"/> when it was not supplied.</param>
    /// <param name="file">The manifest entry describing the file to download.</param>
    /// <returns>The absolute path the decrypted file should be written to.</returns>
    /// <exception cref="DownloadCommandException">Thrown when the announced file name cannot be sanitized.</exception>
    public static String Resolve(String? outOption, ShareManifestFileContract file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (String.IsNullOrWhiteSpace(outOption))
        {
            return CombineWithinDirectory(Path.GetFullPath(Environment.CurrentDirectory), ResolveAnnouncedFileName(file));
        }

        // A trailing separator names a directory even when it does not exist yet; an existing path names one
        // whether or not the user typed the separator. Everything else is an explicit destination file, whose
        // name is taken exactly as given.
        return Path.EndsInDirectorySeparator(outOption) || Directory.Exists(outOption)
            ? CombineWithinDirectory(Path.GetFullPath(outOption), ResolveAnnouncedFileName(file))
            : Path.GetFullPath(outOption);
    }

    /// <summary>
    /// Derives the sanitized file name a share announces for <paramref name="file"/>, falling back to the file ID
    /// and finally to a fixed name when the manifest announces no usable name at all.
    /// </summary>
    /// <exception cref="DownloadCommandException">
    /// Thrown when the manifest announces a name that cannot be sanitized, for example <c>..</c> or a bare separator.
    /// </exception>
    internal static String ResolveAnnouncedFileName(ShareManifestFileContract file)
    {
        // An absent name is not the server's fault; a present but unusable one is, and must not be silently replaced.
        if (String.IsNullOrWhiteSpace(file.FileName))
        {
            return SafeFileName.TrySanitize(file.FileId, out var safeFallbackName) ? safeFallbackName : FallbackFileName;
        }

        return SafeFileName.TrySanitize(file.FileName, out var safeFileName)
            ? safeFileName
            : throw new DownloadCommandException(
                "The shared file name cannot be used as a safe output file name. Pass --out to choose a destination.");
    }

    private static String CombineWithinDirectory(String directoryPath, String fileName)
    {
        var destinationPath = Path.GetFullPath(Path.Combine(directoryPath, fileName));

        // Belt and braces: the announced name is already reduced to a separator-free leaf, so it cannot escape.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var directoryPrefix = Path.EndsInDirectorySeparator(directoryPath)
            ? directoryPath
            : directoryPath + Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(directoryPrefix, comparison))
        {
            throw new DownloadCommandException("The resolved output path escapes the output directory.");
        }

        return destinationPath;
    }
}
