// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Output;

using System.Text;

/// <summary>
/// Writes sensitive credential documents to disk with restrictive permissions and atomic replacement.
/// </summary>
/// <remarks>
/// The file is written to a temporary sibling, assigned owner-only permissions on supported platforms,
/// flushed to durable storage, and atomically moved into place. On Windows the owner-only restriction is a
/// documented limitation: the move is still atomic, but file-system ACLs are not narrowed by this writer.
/// </remarks>
internal static class SecretsFileWriter
{
    /// <summary>
    /// Validates that the target path can be written before any network work begins.
    /// </summary>
    /// <param name="path">The destination file.</param>
    /// <param name="force">When <see langword="true"/>, allows overwriting an existing file.</param>
    /// <exception cref="SecretsFileException">Thrown when the file exists without <paramref name="force"/> or its directory is missing.</exception>
    public static void EnsureWritable(FileInfo path, Boolean force)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = path.Directory;
        if (directory is not null && !directory.Exists)
        {
            throw new SecretsFileException("The output directory does not exist.");
        }

        if (path.Exists && !force)
        {
            throw new SecretsFileException("Refusing to overwrite an existing file. Pass --force to overwrite.");
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="path"/> with owner-only permissions where supported.
    /// </summary>
    /// <param name="path">The destination file.</param>
    /// <param name="content">The document content.</param>
    /// <param name="force">When <see langword="true"/>, an existing destination is replaced.</param>
    /// <exception cref="SecretsFileException">Thrown when the file cannot be written.</exception>
    public static void WriteAtomic(FileInfo path, String content, Boolean force)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        var directory = path.Directory?.FullName ?? Directory.GetCurrentDirectory();
        var tempPath = Path.Combine(directory, $".{path.Name}.{Guid.NewGuid():N}.tmp");

        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };

            // Create the file owner-only from the outset so the secret is never written through a handle
            // that was opened while the file was still group/world-readable.
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, streamOptions))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes);
                stream.Flush(true);
            }

            File.Move(tempPath, path.FullName, force);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new SecretsFileException("The credentials file could not be written.", exception);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(String path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temporary file; ignore secondary failures.
        }
    }
}

/// <summary>
/// Raised when a credential document cannot be written to disk.
/// </summary>
internal sealed class SecretsFileException : Exception
{
    public SecretsFileException(String message)
        : base(message) { }

    public SecretsFileException(String message, Exception innerException)
        : base(message, innerException) { }
}
