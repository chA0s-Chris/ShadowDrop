// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using System.Text.Json.Serialization;

internal sealed class MultipartUploadRequest
{
    [JsonPropertyName("algorithmId")]
    public String? AlgorithmId { get; init; }

    [JsonPropertyName("chunkCount")]
    public Int64 ChunkCount { get; init; }

    [JsonPropertyName("chunkSize")]
    public Int32 ChunkSize { get; init; }

    [JsonPropertyName("contentType")]
    public String? ContentType { get; init; }

    [JsonPropertyName("encryptedLength")]
    public Int64 EncryptedLength { get; init; }

    [JsonPropertyName("encryptionFormatVersion")]
    public String? EncryptionFormatVersion { get; init; }

    [JsonPropertyName("fileId")]
    public Guid FileId { get; init; }

    [JsonPropertyName("kdfSalt")]
    public String? KdfSalt { get; init; }

    [JsonPropertyName("originalFileName")]
    public String? OriginalFileName { get; init; }

    [JsonPropertyName("plaintextLength")]
    public Int64 PlaintextLength { get; init; }

    [JsonPropertyName("plaintextSha256")]
    public String? PlaintextSha256 { get; init; }
}
