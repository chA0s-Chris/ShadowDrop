// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

/// <summary>
/// Describes the chunk indexes and offsets covering a plaintext range.
/// </summary>
public sealed record ChunkRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkRange"/> record.
    /// </summary>
    /// <param name="firstChunkIndex">The first chunk index in the range.</param>
    /// <param name="lastChunkIndex">The last chunk index in the range.</param>
    /// <param name="offsetInFirstChunk">The starting offset within the first chunk.</param>
    /// <param name="endOffsetInLastChunk">The inclusive ending offset within the last chunk.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any argument is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when the chunk indexes or offsets are inconsistent.</exception>
    public ChunkRange(Int64 firstChunkIndex, Int64 lastChunkIndex, Int32 offsetInFirstChunk, Int32 endOffsetInLastChunk)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstChunkIndex, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastChunkIndex, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(offsetInFirstChunk, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(endOffsetInLastChunk, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastChunkIndex, firstChunkIndex);

        if (firstChunkIndex == lastChunkIndex && offsetInFirstChunk > endOffsetInLastChunk)
        {
            throw new ArgumentException(
                "For single-chunk ranges, the starting offset must not exceed the ending offset.",
                nameof(offsetInFirstChunk));
        }

        FirstChunkIndex = firstChunkIndex;
        LastChunkIndex = lastChunkIndex;
        OffsetInFirstChunk = offsetInFirstChunk;
        EndOffsetInLastChunk = endOffsetInLastChunk;
    }

    /// <summary>
    /// Gets the inclusive ending offset within the last chunk.
    /// </summary>
    public Int32 EndOffsetInLastChunk { get; }

    /// <summary>
    /// Gets the first chunk index in the range.
    /// </summary>
    public Int64 FirstChunkIndex { get; }

    /// <summary>
    /// Gets the last chunk index in the range.
    /// </summary>
    public Int64 LastChunkIndex { get; }

    /// <summary>
    /// Gets the starting offset within the first chunk.
    /// </summary>
    public Int32 OffsetInFirstChunk { get; }
}
