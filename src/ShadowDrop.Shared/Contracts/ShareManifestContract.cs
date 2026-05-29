// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Describes the downloadable files exposed by a public share.
/// </summary>
public sealed record ShareManifestContract
{
    /// <summary>
    /// Gets or sets the files available through the share.
    /// </summary>
    [JsonPropertyName("files")]
    [JsonPropertyOrder(0)]
    public IReadOnlyList<ShareManifestFileContract>? Files { get; init; }
}

/// <summary>
/// Describes one downloadable file exposed by a public share.
/// </summary>
public sealed record ShareManifestFileContract
{
    /// <summary>
    /// Gets or sets the algorithm identifier.
    /// </summary>
    [JsonPropertyName("algorithmId")]
    [JsonPropertyOrder(4)]
    public String? AlgorithmId { get; init; }

    /// <summary>
    /// Gets or sets the chunk count.
    /// </summary>
    [JsonPropertyName("chunkCount")]
    [JsonPropertyOrder(6)]
    public Int64 ChunkCount { get; init; }

    /// <summary>
    /// Gets or sets the configured plaintext chunk size.
    /// </summary>
    [JsonPropertyName("chunkSize")]
    [JsonPropertyOrder(5)]
    public Int32 ChunkSize { get; init; }

    /// <summary>
    /// Gets or sets the shared encryption format version.
    /// </summary>
    [JsonPropertyName("encryptionFormatVersion")]
    [JsonPropertyOrder(3)]
    public String? EncryptionFormatVersion { get; init; }

    /// <summary>
    /// Gets or sets the file identifier.
    /// </summary>
    [JsonPropertyName("fileId")]
    [JsonPropertyOrder(0)]
    public String? FileId { get; init; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    [JsonPropertyName("fileName")]
    [JsonPropertyOrder(1)]
    public String? FileName { get; init; }

    /// <summary>
    /// Gets or sets the non-secret file-level KDF salt encoded as Base64.
    /// </summary>
    [JsonPropertyName("kdfSalt")]
    [JsonPropertyOrder(7)]
    public String? KdfSalt { get; init; }

    /// <summary>
    /// Gets or sets the file length in bytes.
    /// </summary>
    [JsonPropertyName("length")]
    [JsonPropertyOrder(2)]
    public Int64 Length { get; init; }

    /// <summary>
    /// Gets or sets the optional lowercase hexadecimal plaintext SHA-256 digest.
    /// </summary>
    [JsonPropertyName("plaintextSha256")]
    [JsonPropertyOrder(8)]
    public String? PlaintextSha256 { get; init; }
}
