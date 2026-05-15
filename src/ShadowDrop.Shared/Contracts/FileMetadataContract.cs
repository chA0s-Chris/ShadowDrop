// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Describes the shared wire and file metadata needed to decrypt a file.
/// </summary>
public sealed record FileMetadataContract
{
    /// <summary>
    /// Gets or sets the algorithm identifier.
    /// </summary>
    [JsonPropertyName("algorithmId")]
    [JsonPropertyOrder(3)]
    public String? AlgorithmId { get; init; }

    /// <summary>
    /// Gets or sets the chunk count.
    /// </summary>
    [JsonPropertyName("chunkCount")]
    [JsonPropertyOrder(5)]
    public Int64 ChunkCount { get; init; }

    /// <summary>
    /// Gets or sets the configured plaintext chunk size.
    /// </summary>
    [JsonPropertyName("chunkSize")]
    [JsonPropertyOrder(4)]
    public Int32 ChunkSize { get; init; }

    /// <summary>
    /// Gets or sets the shared encryption format version.
    /// </summary>
    [JsonPropertyName("encryptionFormatVersion")]
    [JsonPropertyOrder(2)]
    public String? EncryptionFormatVersion { get; init; }

    /// <summary>
    /// Gets or sets the file identifier.
    /// </summary>
    [JsonPropertyName("fileId")]
    [JsonPropertyOrder(1)]
    public String? FileId { get; init; }

    /// <summary>
    /// Gets or sets the non-secret share-level KDF salt encoded as Base64.
    /// </summary>
    [JsonPropertyName("kdfSalt")]
    [JsonPropertyOrder(6)]
    public String? KdfSalt { get; init; }

    /// <summary>
    /// Gets or sets the optional lowercase hexadecimal plaintext SHA-256 digest.
    /// </summary>
    [JsonPropertyName("plaintextSha256")]
    [JsonPropertyOrder(7)]
    public String? PlaintextSha256 { get; init; }

    /// <summary>
    /// Gets or sets the share identifier.
    /// </summary>
    [JsonPropertyName("shareId")]
    [JsonPropertyOrder(0)]
    public String? ShareId { get; init; }
}
