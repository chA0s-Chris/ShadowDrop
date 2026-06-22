// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Text.Json.Serialization;

/// <summary>
/// Represents one file entry in a ShadowDrop queue file.
/// </summary>
public sealed record QueueFileEntry
{
    /// <summary>
    /// Gets or sets the file identifier.
    /// </summary>
    [JsonPropertyName("fileId")]
    [JsonPropertyOrder(2)]
    public String? FileId { get; init; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    [JsonPropertyName("fileName")]
    [JsonPropertyOrder(3)]
    public String? FileName { get; init; }

    /// <summary>
    /// Gets or sets the file length in bytes.
    /// </summary>
    [JsonPropertyName("length")]
    [JsonPropertyOrder(4)]
    public Int64? Length { get; init; }

    /// <summary>
    /// Gets or sets the local output path for the decrypted file.
    /// </summary>
    [JsonPropertyName("outputPath")]
    [JsonPropertyOrder(5)]
    public String? OutputPath { get; init; }

    /// <summary>
    /// Gets or sets the optional lowercase hexadecimal plaintext SHA-256 digest.
    /// </summary>
    [JsonPropertyName("plaintextSha256")]
    [JsonPropertyOrder(6)]
    public String? PlaintextSha256 { get; init; }

    /// <summary>
    /// Gets or sets the base URL of the ShadowDrop server hosting the share.
    /// </summary>
    [JsonPropertyName("serverUrl")]
    [JsonPropertyOrder(0)]
    public String? ServerUrl { get; init; }

    /// <summary>
    /// Gets or sets the public share token used to download the share. The server base URL is stored separately in <see cref="ServerUrl"/>.
    /// </summary>
    [JsonPropertyName("shareToken")]
    [JsonPropertyOrder(1)]
    public String? ShareToken { get; init; }
}
