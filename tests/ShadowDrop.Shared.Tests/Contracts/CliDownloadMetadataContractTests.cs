// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;

public sealed class CliDownloadMetadataContractTests
{
    [Test]
    public void CliDownloadMetadataContract_ShouldExposeAssignedValues()
    {
        var metadata = new CliDownloadMetadataContract
        {
            FirstChunkIndex = 2,
            LastChunkIndex = 5,
            RequestedRange = new()
            {
                Start = 131072,
                End = 262144
            },
            TotalPlaintextSize = 1048576,
            ChunkSize = 65536,
            FinalChunkPlaintextLength = 32768
        };

        metadata.FirstChunkIndex.Should().Be(2);
        metadata.LastChunkIndex.Should().Be(5);
        metadata.RequestedRange.Start.Should().Be(131072);
        metadata.RequestedRange.End.Should().Be(262144);
        metadata.TotalPlaintextSize.Should().Be(1048576);
        metadata.ChunkSize.Should().Be(65536);
        metadata.FinalChunkPlaintextLength.Should().Be(32768);
    }
}
