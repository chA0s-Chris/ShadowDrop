// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Text.Json.Serialization;

internal sealed record UploadMetadataPayload(
    [property: JsonPropertyName("fileId")]
    [property: JsonPropertyOrder(0)]
    Guid FileId,
    [property: JsonPropertyName("originalFileName")]
    [property: JsonPropertyOrder(1)]
    String OriginalFileName,
    [property: JsonPropertyName("plaintextLength")]
    [property: JsonPropertyOrder(2)]
    Int64 PlaintextLength,
    [property: JsonPropertyName("encryptedLength")]
    [property: JsonPropertyOrder(3)]
    Int64 EncryptedLength,
    [property: JsonPropertyName("contentType")]
    [property: JsonPropertyOrder(4)]
    String ContentType,
    [property: JsonPropertyName("encryptionFormatVersion")]
    [property: JsonPropertyOrder(5)]
    String EncryptionFormatVersion,
    [property: JsonPropertyName("algorithmId")]
    [property: JsonPropertyOrder(6)]
    String AlgorithmId,
    [property: JsonPropertyName("chunkSize")]
    [property: JsonPropertyOrder(7)]
    Int32 ChunkSize,
    [property: JsonPropertyName("chunkCount")]
    [property: JsonPropertyOrder(8)]
    Int64 ChunkCount,
    [property: JsonPropertyName("kdfSalt")]
    [property: JsonPropertyOrder(9)]
    String KdfSalt,
    [property: JsonPropertyName("plaintextSha256")]
    [property: JsonPropertyOrder(10)]
    String? PlaintextSha256);
