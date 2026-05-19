// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Describes the streamed CLI download metadata carried in HTTP headers.
/// </summary>
public sealed record CliDownloadMetadataContract
{
    /// <summary>
    /// Gets the configured plaintext chunk size.
    /// </summary>
    public Int32 ChunkSize { get; init; }

    /// <summary>
    /// Gets the plaintext length of the final chunk.
    /// </summary>
    public Int32 FinalChunkPlaintextLength { get; init; }

    /// <summary>
    /// Gets the first encrypted chunk index included in the response body.
    /// </summary>
    public Int64 FirstChunkIndex { get; init; }

    /// <summary>
    /// Gets the last encrypted chunk index included in the response body.
    /// </summary>
    public Int64 LastChunkIndex { get; init; }

    /// <summary>
    /// Gets the requested plaintext byte range.
    /// </summary>
    public RequestedPlaintextRangeContract RequestedRange { get; init; } = new();

    /// <summary>
    /// Gets the total plaintext file size.
    /// </summary>
    public Int64 TotalPlaintextSize { get; init; }
}
