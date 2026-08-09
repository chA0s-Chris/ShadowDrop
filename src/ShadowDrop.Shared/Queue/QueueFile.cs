// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Text.Json.Serialization;

/// <summary>
/// Represents the shared ShadowDrop queue file format. A queue describes exactly one share, so the server URL,
/// the share token, and any embedded credentials are queue-scoped rather than repeated per file entry.
/// </summary>
public sealed record QueueFile
{
    /// <summary>
    /// Gets or sets the optional embedded download credentials for the queue's share.
    /// </summary>
    /// <remarks>
    /// Present only for self-contained queues created with <c>--embed-secrets</c>. Secret-free queues omit this
    /// object entirely and require the download credentials to be supplied through separate inputs.
    /// </remarks>
    [JsonPropertyName("credentials")]
    [JsonPropertyOrder(4)]
    public QueueCredentials? Credentials { get; init; }

    /// <summary>
    /// Gets or sets the queue file entries.
    /// </summary>
    [JsonPropertyName("files")]
    [JsonPropertyOrder(5)]
    public IReadOnlyList<QueueFileEntry>? Files { get; init; }

    /// <summary>
    /// Gets or sets the queue file format version.
    /// </summary>
    [JsonPropertyName("queueVersion")]
    [JsonPropertyOrder(1)]
    public String? QueueVersion { get; init; }

    /// <summary>
    /// Gets or sets the base URL of the ShadowDrop server hosting the queue's share.
    /// </summary>
    [JsonPropertyName("serverUrl")]
    [JsonPropertyOrder(2)]
    public String? ServerUrl { get; init; }

    /// <summary>
    /// Gets or sets the ShadowDrop marker version.
    /// </summary>
    [JsonPropertyName("shadowDrop")]
    [JsonPropertyOrder(0)]
    public String? ShadowDrop { get; init; }

    /// <summary>
    /// Gets or sets the public share token used to download the queue's share. The server base URL is stored
    /// separately in <see cref="ServerUrl"/>.
    /// </summary>
    [JsonPropertyName("shareToken")]
    [JsonPropertyOrder(3)]
    public String? ShareToken { get; init; }
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
