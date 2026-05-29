// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Text.Json;

public sealed class ShareManifestContractTests
{
    [Test]
    public void Serialize_ShouldUseExpectedPropertyNames()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(manifest);
        using var document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("files");
        document.RootElement.GetProperty("files")[0].EnumerateObject().Select(property => property.Name).Should()
                .Equal("fileId",
                       "fileName",
                       "length",
                       "encryptionFormatVersion",
                       "algorithmId",
                       "chunkSize",
                       "chunkCount",
                       "kdfSalt",
                       "plaintextSha256");
    }

    [Test]
    public void SerializeAndDeserialize_ShouldRoundTripManifest()
    {
        var manifest = CreateManifest();

        var json = JsonSerializer.Serialize(manifest);
        var result = JsonSerializer.Deserialize<ShareManifestContract>(json);

        result.Should().BeEquivalentTo(manifest);
    }

    private static ShareManifestContract CreateManifest() =>
        new()
        {
            Files =
            [
                new()
                {
                    FileId = "01234567-89ab-cdef-0123-456789abcdef",
                    FileName = "report.txt",
                    Length = 4096,
                    EncryptionFormatVersion = FormatConstants.EncryptionFormatVersion,
                    AlgorithmId = FormatConstants.Aes256GcmAlgorithmId,
                    ChunkSize = 1024,
                    ChunkCount = 4,
                    KdfSalt = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=",
                    PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
            ]
        };
}
