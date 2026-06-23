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
    /// Gets or sets the optional embedded download credentials shared by every entry.
    /// </summary>
    /// <remarks>
    /// Present only for self-contained queues created with <c>--embed-secrets</c>. Secret-free queues omit this
    /// object entirely and require the download credentials to be supplied through separate inputs.
    /// </remarks>
    [JsonPropertyName("credentials")]
    [JsonPropertyOrder(2)]
    public QueueCredentials? Credentials { get; init; }

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
}

/// <summary>
/// The embedded download credentials carried by a self-contained queue.
/// </summary>
public sealed record QueueCredentials
{
    /// <summary>
    /// Gets or sets the optional download bearer token required by the share.
    /// </summary>
    [JsonPropertyName("downloadBearerToken")]
    [JsonPropertyOrder(1)]
    public String? DownloadBearerToken { get; init; }

    /// <summary>
    /// Gets or sets the plaintext share key as lowercase hexadecimal key material.
    /// </summary>
    [JsonPropertyName("shareKey")]
    [JsonPropertyOrder(0)]
    public String? ShareKey { get; init; }
}
