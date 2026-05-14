// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Crypto;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Crypto;

public sealed class ChunkRangeMappingTests
{
    [TestCase(0L, 64L, 64, 0L, 0L, 0, 63, TestName = "GetChunkRange_MapsSingleFullChunk")]
    [TestCase(0L, 192L, 64, 0L, 2L, 0, 63, TestName = "GetChunkRange_MapsThreeFullChunks")]
    [TestCase(11L, 13L, 64, 0L, 0L, 11, 23, TestName = "GetChunkRange_MapsSubChunkRangeWithinSingleChunk")]
    [TestCase(64L, 40L, 64, 1L, 1L, 0, 39, TestName = "GetChunkRange_MapsBoundaryStartToMidSecondChunk")]
    [TestCase(21L, 180L, 64, 0L, 3L, 21, 8, TestName = "GetChunkRange_MapsNonAlignedRangeAcrossFourChunks")]
    [TestCase(0L, 1L, 64, 0L, 0L, 0, 0, TestName = "GetChunkRange_MapsMinimumRange")]
    [TestCase(63L, 2L, 64, 0L, 1L, 63, 0, TestName = "GetChunkRange_MapsBoundaryCrossingRange")]
    public void GetChunkRange_ShouldMapPlaintextRangeCorrectly(
        Int64 plaintextOffset,
        Int64 plaintextLength,
        Int32 chunkSize,
        Int64 expectedFirstChunkIndex,
        Int64 expectedLastChunkIndex,
        Int32 expectedOffsetInFirstChunk,
        Int32 expectedEndOffsetInLastChunk)
    {
        var chunkRange = ChunkEncryptionService.GetChunkRange(plaintextOffset, plaintextLength, chunkSize);

        chunkRange.FirstChunkIndex.Should().Be(expectedFirstChunkIndex);
        chunkRange.LastChunkIndex.Should().Be(expectedLastChunkIndex);
        chunkRange.OffsetInFirstChunk.Should().Be(expectedOffsetInFirstChunk);
        chunkRange.EndOffsetInLastChunk.Should().Be(expectedEndOffsetInLastChunk);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void GetChunkRange_ShouldThrowArgumentOutOfRangeException_WhenChunkSizeIsNotPositive(Int32 chunkSize)
    {
        var act = () => ChunkEncryptionService.GetChunkRange(0, 1, chunkSize);

        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(chunkSize));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void GetChunkRange_ShouldThrowArgumentOutOfRangeException_WhenPlaintextLengthIsNotPositive(Int64 plaintextLength)
    {
        var act = () => ChunkEncryptionService.GetChunkRange(0, plaintextLength, 64);

        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName(nameof(plaintextLength));
    }

    [Test]
    public void GetChunkRange_ShouldThrowArgumentOutOfRangeException_WhenPlaintextOffsetIsNegative()
    {
        var act = () => ChunkEncryptionService.GetChunkRange(-1, 1, 64);

        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName("plaintextOffset");
    }

    [Test]
    public void GetChunkRange_ShouldThrowArgumentOutOfRangeException_WhenRequestedRangeOverflowsInt64()
    {
        var act = () => ChunkEncryptionService.GetChunkRange(Int64.MaxValue - 1, 3, 64);

        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName("plaintextLength");
    }
}
