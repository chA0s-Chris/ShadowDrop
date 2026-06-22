// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Output;

using System.Text;

/// <summary>
/// Writes a file atomically, optionally with owner-only permissions for sensitive content.
/// </summary>
/// <remarks>
/// The content is written to a temporary sibling, flushed to durable storage, and atomically moved into
/// place. When <c>ownerOnly</c> is requested the temporary file is created owner-only from the outset on
/// supported platforms (Windows is a documented limitation: the move is still atomic, but file-system ACLs
/// are not narrowed by this writer).
/// </remarks>
internal static class AtomicFileWriter
{
    /// <summary>
    /// Validates that the target path can be written before any expensive work begins.
    /// </summary>
    /// <param name="path">The destination file.</param>
    /// <param name="force">When <see langword="true"/>, allows overwriting an existing file.</param>
    /// <exception cref="AtomicFileException">Thrown when the file exists without <paramref name="force"/> or its directory is missing.</exception>
    public static void EnsureWritable(FileInfo path, Boolean force)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = path.Directory;
        if (directory is not null && !directory.Exists)
        {
            throw new AtomicFileException("The output directory does not exist.");
        }

        if (path.Exists && !force)
        {
            throw new AtomicFileException("Refusing to overwrite an existing file. Pass --force to overwrite.");
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="content"/> to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The destination file.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="force">When <see langword="true"/>, an existing destination is replaced.</param>
    /// <param name="ownerOnly">When <see langword="true"/>, the file is created with owner-only permissions where supported.</param>
    /// <exception cref="AtomicFileException">Thrown when the file cannot be written.</exception>
    public static void WriteAtomic(FileInfo path, String content, Boolean force, Boolean ownerOnly)
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

            // Create the file owner-only from the outset so sensitive content is never written through a
            // handle that was opened while the file was still group/world-readable.
            if (ownerOnly && !OperatingSystem.IsWindows())
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
            throw new AtomicFileException("The file could not be written.", exception);
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
/// Raised when a file cannot be written atomically.
/// </summary>
internal sealed class AtomicFileException : Exception
{
    public AtomicFileException(String message)
        : base(message) { }

    public AtomicFileException(String message, Exception innerException)
        : base(message, innerException) { }
}
