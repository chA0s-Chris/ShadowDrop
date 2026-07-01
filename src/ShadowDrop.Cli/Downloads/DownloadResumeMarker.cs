// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using System.Text.Json.Serialization;

internal sealed record DownloadResumeMarker(
    [property: JsonPropertyName("version")]
    Int32 Version,
    [property: JsonPropertyName("serverUrl")]
    String ServerUrl,
    [property: JsonPropertyName("shareToken")]
    String ShareToken,
    [property: JsonPropertyName("fileId")]
    String FileId,
    [property: JsonPropertyName("fileName")]
    String? FileName,
    [property: JsonPropertyName("fileLength")]
    Int64 FileLength,
    [property: JsonPropertyName("kdfSalt")]
    String? KdfSalt,
    [property: JsonPropertyName("plaintextSha256")]
    String? PlaintextSha256);
