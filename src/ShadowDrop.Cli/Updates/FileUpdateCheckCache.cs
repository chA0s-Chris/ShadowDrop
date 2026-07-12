// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Output;
using System.Text.Json;

/// <summary>
/// File-backed <see cref="IUpdateCheckCache"/>. Reads tolerate a missing or corrupt cache file and writes
/// are atomic and best-effort, so cache trouble can never break a command or an update check.
/// </summary>
internal sealed class FileUpdateCheckCache(UpdateCheckCachePathResolver pathResolver) : IUpdateCheckCache
{
    public UpdateCheckRecord? Read()
    {
        try
        {
            var path = pathResolver.GetCacheFilePath();
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, CliJsonSerializerContext.Default.UpdateCheckRecord);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Write(UpdateCheckRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            var path = pathResolver.GetCacheFilePath();
            if (path is null)
            {
                return;
            }

            var cacheFile = new FileInfo(path);
            cacheFile.Directory?.Create();
            AtomicFileWriter.WriteAtomic(cacheFile,
                                         JsonSerializer.Serialize(record, CliJsonSerializerContext.Default.UpdateCheckRecord),
                                         true,
                                         false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AtomicFileException)
        {
            // Best-effort persistence; the next invocation simply checks again.
        }
    }
}
