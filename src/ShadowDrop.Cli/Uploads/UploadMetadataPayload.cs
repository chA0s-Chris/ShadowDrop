// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Text.Json.Serialization;

internal sealed record UploadMetadataPayload(
    [property: JsonPropertyName("fileId"), JsonPropertyOrder(0)]
    Guid FileId,
    [property: JsonPropertyName("originalFileName"), JsonPropertyOrder(1)]
    String OriginalFileName,
    [property: JsonPropertyName("plaintextLength"), JsonPropertyOrder(2)]
    Int64 PlaintextLength,
    [property: JsonPropertyName("encryptedLength"), JsonPropertyOrder(3)]
    Int64 EncryptedLength,
    [property: JsonPropertyName("contentType"), JsonPropertyOrder(4)]
    String ContentType,
    [property: JsonPropertyName("encryptionFormatVersion"), JsonPropertyOrder(5)]
    String EncryptionFormatVersion,
    [property: JsonPropertyName("algorithmId"), JsonPropertyOrder(6)]
    String AlgorithmId,
    [property: JsonPropertyName("chunkSize"), JsonPropertyOrder(7)]
    Int32 ChunkSize,
    [property: JsonPropertyName("chunkCount"), JsonPropertyOrder(8)]
    Int64 ChunkCount,
    [property: JsonPropertyName("kdfSalt"), JsonPropertyOrder(9)]
    String KdfSalt,
    [property: JsonPropertyName("plaintextSha256"), JsonPropertyOrder(10)]
    String? PlaintextSha256);
