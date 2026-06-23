// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Results;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

/// <summary>
/// Serializes an <see cref="UploadCommandResult"/> to standard output, used by the upload and share-creation
/// command surfaces for their JSON output mode.
/// </summary>
internal static class UploadResultWriter
{
    public static Task WriteAsync(TextWriter standardOut, UploadCommandResult result) =>
        standardOut.WriteLineAsync(JsonSerializer.Serialize(result, CliJsonSerializerContext.Default.UploadCommandResult));
}
