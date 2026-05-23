// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Text.Json.Serialization;

internal sealed record UploadMetadataPayload(
    Guid FileId,
    String OriginalFileName,
    Int64 PlaintextLength,
    Int64 EncryptedLength,
    String ContentType,
    String EncryptionFormatVersion,
    String AlgorithmId,
    Int32 ChunkSize,
    Int64 ChunkCount,
    String KdfSalt,
    String? PlaintextSha256)
{
    [JsonPropertyName("algorithmId")]
    public String AlgorithmId { get; init; } = AlgorithmId;

    [JsonPropertyName("chunkCount")]
    public Int64 ChunkCount { get; init; } = ChunkCount;

    [JsonPropertyName("chunkSize")]
    public Int32 ChunkSize { get; init; } = ChunkSize;

    [JsonPropertyName("contentType")]
    public String ContentType { get; init; } = ContentType;

    [JsonPropertyName("encryptedLength")]
    public Int64 EncryptedLength { get; init; } = EncryptedLength;

    [JsonPropertyName("encryptionFormatVersion")]
    public String EncryptionFormatVersion { get; init; } = EncryptionFormatVersion;

    [JsonPropertyName("fileId")]
    public Guid FileId { get; init; } = FileId;

    [JsonPropertyName("kdfSalt")]
    public String KdfSalt { get; init; } = KdfSalt;

    [JsonPropertyName("originalFileName")]
    public String OriginalFileName { get; init; } = OriginalFileName;

    [JsonPropertyName("plaintextLength")]
    public Int64 PlaintextLength { get; init; } = PlaintextLength;

    [JsonPropertyName("plaintextSha256")]
    public String? PlaintextSha256 { get; init; } = PlaintextSha256;
}
