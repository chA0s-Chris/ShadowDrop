// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Text.Json.Serialization;

/// <summary>
/// Represents one file entry in a ShadowDrop queue file. The share the entry belongs to is described once by the
/// owning <see cref="QueueFile"/>.
/// </summary>
public sealed record QueueFileEntry
{
    /// <summary>
    /// Gets or sets the file identifier.
    /// </summary>
    [JsonPropertyName("fileId")]
    [JsonPropertyOrder(0)]
    public String? FileId { get; init; }

    /// <summary>
    /// Gets or sets the server-announced file name.
    /// </summary>
    [JsonPropertyName("fileName")]
    [JsonPropertyOrder(1)]
    public String? FileName { get; init; }

    /// <summary>
    /// Gets or sets the file length in bytes.
    /// </summary>
    [JsonPropertyName("length")]
    [JsonPropertyOrder(2)]
    public Int64? Length { get; init; }

    /// <summary>
    /// Gets or sets the optional local output path for the decrypted file, relative to the download output root
    /// and using <c>/</c> as its directory separator.
    /// </summary>
    /// <remarks>
    /// Omitted when the destination is exactly <see cref="FileName"/>; use <see cref="QueueOutputPath.Resolve"/>
    /// to obtain the effective destination of an entry.
    /// </remarks>
    [JsonPropertyName("outputPath")]
    [JsonPropertyOrder(3)]
    public String? OutputPath { get; init; }

    /// <summary>
    /// Gets or sets the optional lowercase hexadecimal plaintext SHA-256 digest.
    /// </summary>
    [JsonPropertyName("plaintextSha256")]
    [JsonPropertyOrder(4)]
    public String? PlaintextSha256 { get; init; }
}
