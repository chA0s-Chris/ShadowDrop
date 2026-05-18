// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Describes the deterministic encrypted-subset response returned to CLI download clients.
/// </summary>
public sealed record CliResumableDownloadContract
{
    /// <summary>
    /// Gets or sets the configured plaintext chunk size.
    /// </summary>
    [JsonPropertyName("chunkSize")]
    [JsonPropertyOrder(5)]
    public Int32 ChunkSize { get; init; }

    /// <summary>
    /// Gets or sets the encrypted chunk payload encoded as Base64.
    /// </summary>
    [JsonPropertyName("encryptedPayload")]
    [JsonPropertyOrder(2)]
    public String? EncryptedPayload { get; init; }

    /// <summary>
    /// Gets or sets the plaintext length of the final chunk.
    /// </summary>
    [JsonPropertyName("finalChunkPlaintextLength")]
    [JsonPropertyOrder(6)]
    public Int32 FinalChunkPlaintextLength { get; init; }

    /// <summary>
    /// Gets or sets the first encrypted chunk index included in the payload.
    /// </summary>
    [JsonPropertyName("firstChunkIndex")]
    [JsonPropertyOrder(0)]
    public Int64 FirstChunkIndex { get; init; }

    /// <summary>
    /// Gets or sets the last encrypted chunk index included in the payload.
    /// </summary>
    [JsonPropertyName("lastChunkIndex")]
    [JsonPropertyOrder(1)]
    public Int64 LastChunkIndex { get; init; }

    /// <summary>
    /// Gets or sets the requested plaintext byte range.
    /// </summary>
    [JsonPropertyName("requestedRange")]
    [JsonPropertyOrder(3)]
    public RequestedPlaintextRangeContract? RequestedRange { get; init; }

    /// <summary>
    /// Gets or sets the total plaintext file size.
    /// </summary>
    [JsonPropertyName("totalPlaintextSize")]
    [JsonPropertyOrder(4)]
    public Int64 TotalPlaintextSize { get; init; }
}
