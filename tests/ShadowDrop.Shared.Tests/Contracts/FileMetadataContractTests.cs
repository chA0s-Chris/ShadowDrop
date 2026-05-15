// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Text.Json;

public sealed class FileMetadataContractTests
{
    [Test]
    public void Deserialize_ShouldAllowMissingPlaintextSha256()
    {
        const String json = """
                            {
                              "shareId": "share-123",
                              "fileId": "file-456",
                              "encryptionFormatVersion": "1.0",
                              "algorithmId": "aes-256-gcm",
                              "chunkSize": 4096,
                              "chunkCount": 3,
                              "kdfSalt": "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="
                            }
                            """;

        var result = JsonSerializer.Deserialize<FileMetadataContract>(json);

        result.Should().NotBeNull();
        result!.ShareId.Should().Be("share-123");
        result.FileId.Should().Be("file-456");
        result.EncryptionFormatVersion.Should().Be(FormatConstants.EncryptionFormatVersion);
        result.AlgorithmId.Should().Be(FormatConstants.Aes256GcmAlgorithmId);
        result.ChunkSize.Should().Be(4096);
        result.ChunkCount.Should().Be(3);
        result.KdfSalt.Should().Be("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=");
        result.PlaintextSha256.Should().BeNull();
    }

    [Test]
    public void Serialize_ShouldUseExpectedPropertyNames()
    {
        var metadata = CreateMetadataContract();

        var json = JsonSerializer.Serialize(metadata);
        using var document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(property => property.Name).Should()
                .Equal("shareId",
                       "fileId",
                       "encryptionFormatVersion",
                       "algorithmId",
                       "chunkSize",
                       "chunkCount",
                       "kdfSalt",
                       "plaintextSha256");
    }

    [Test]
    public void SerializeAndDeserialize_ShouldRoundTripAllMetadata()
    {
        var metadata = CreateMetadataContract();

        var json = JsonSerializer.Serialize(metadata);
        var result = JsonSerializer.Deserialize<FileMetadataContract>(json);

        result.Should().BeEquivalentTo(metadata);
    }

    private static FileMetadataContract CreateMetadataContract()
    {
        return new()
        {
            ShareId = "share-123",
            FileId = "file-456",
            EncryptionFormatVersion = FormatConstants.EncryptionFormatVersion,
            AlgorithmId = FormatConstants.Aes256GcmAlgorithmId,
            ChunkSize = 4096,
            ChunkCount = 3,
            KdfSalt = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=",
            PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        };
    }
}
