// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Text.Json.Serialization;

/// <summary>
/// Represents the shared ShadowDrop queue file format.
/// </summary>
public sealed record QueueFile
{
    /// <summary>
    /// Gets or sets the queue file entries.
    /// </summary>
    [JsonPropertyName("files")]
    [JsonPropertyOrder(4)]
    public IReadOnlyList<QueueFileEntry>? Files { get; init; }

    /// <summary>
    /// Gets or sets the queue file format version.
    /// </summary>
    [JsonPropertyName("queueVersion")]
    [JsonPropertyOrder(1)]
    public String? QueueVersion { get; init; }

    /// <summary>
    /// Gets or sets the ShadowDrop marker version.
    /// </summary>
    [JsonPropertyName("shadowDrop")]
    [JsonPropertyOrder(0)]
    public String? ShadowDrop { get; init; }

    /// <summary>
    /// Gets or sets the share identifier.
    /// </summary>
    [JsonPropertyName("shareId")]
    [JsonPropertyOrder(3)]
    public String? ShareId { get; init; }

    /// <summary>
    /// Gets or sets the target base URL used for the queue.
    /// </summary>
    [JsonPropertyName("target")]
    [JsonPropertyOrder(2)]
    public String? Target { get; init; }
}
