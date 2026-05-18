// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;

public sealed class CliResumableDownloadContractParserTests
{
    [Test]
    public void Parse_ShouldAcceptLargeChunkSpanMetadata_WhenValuesStayWithinContractBounds()
    {
        const String json = """
                            {
                              "firstChunkIndex": 34359738368,
                              "lastChunkIndex": 34359742463,
                              "encryptedPayload": "AQIDBA==",
                              "requestedRange": {
                                "start": 2251799813685248,
                                "end": 2251799813955792
                              },
                              "totalPlaintextSize": 2251799817635840,
                              "chunkSize": 65536,
                              "finalChunkPlaintextLength": 65536
                            }
                            """;

        var result = CliResumableDownloadContractParser.Parse(json);

        result.FirstChunkIndex.Should().Be(34359738368);
        result.LastChunkIndex.Should().Be(34359742463);
        result.RequestedRange.Should().NotBeNull();
        result.RequestedRange!.Start.Should().Be(2251799813685248);
        result.RequestedRange.End.Should().Be(2251799813955792);
        result.TotalPlaintextSize.Should().Be(2251799817635840);
    }

    [Test]
    public void Parse_ShouldReturnContract_WhenPayloadIsValid()
    {
        const String json = """
                            {
                              "firstChunkIndex": 2,
                              "lastChunkIndex": 5,
                              "encryptedPayload": "AQIDBA==",
                              "requestedRange": {
                                "start": 131072,
                                "end": 262144
                              },
                              "totalPlaintextSize": 1048576,
                              "chunkSize": 65536,
                              "finalChunkPlaintextLength": 32768
                            }
                            """;

        var result = CliResumableDownloadContractParser.Parse(json);

        result.FirstChunkIndex.Should().Be(2);
        result.LastChunkIndex.Should().Be(5);
        result.RequestedRange!.Start.Should().Be(131072);
        result.RequestedRange.End.Should().Be(262144);
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenEncryptedPayloadIsMissing()
    {
        const String json = """
                            {
                              "firstChunkIndex": 0,
                              "lastChunkIndex": 0,
                              "requestedRange": {
                                "start": 0,
                                "end": 64
                              },
                              "totalPlaintextSize": 64,
                              "chunkSize": 64,
                              "finalChunkPlaintextLength": 64
                            }
                            """;

        var act = () => CliResumableDownloadContractParser.Parse(json);

        act.Should().Throw<InvalidDataException>()
           .WithMessage("*encrypted chunk data*");
    }
}
