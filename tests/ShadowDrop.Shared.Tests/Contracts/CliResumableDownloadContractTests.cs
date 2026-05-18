// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Text.Json;

public sealed class CliResumableDownloadContractTests
{
    [Test]
    public void Serialize_ShouldUseExpectedPropertyNames()
    {
        var contract = CreateContract();

        var json = JsonSerializer.Serialize(contract, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);
        using var document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(property => property.Name).Should()
                .Equal("firstChunkIndex",
                       "lastChunkIndex",
                       "encryptedPayload",
                       "requestedRange",
                       "totalPlaintextSize",
                       "chunkSize",
                       "finalChunkPlaintextLength");
        document.RootElement.GetProperty("requestedRange")
                .EnumerateObject()
                .Select(property => property.Name)
                .Should()
                .Equal("start", "end");
    }

    [Test]
    public void SerializeAndDeserialize_ShouldRoundTripAllFields()
    {
        var contract = CreateContract();

        var json = JsonSerializer.Serialize(contract, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);
        var result = JsonSerializer.Deserialize(json, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);

        result.Should().BeEquivalentTo(contract);
    }

    [Test]
    public void SharedSerializerContext_ShouldRoundTripAllFields()
    {
        var contract = CreateContract();

        var json = JsonSerializer.SerializeToUtf8Bytes(contract, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);
        var result = JsonSerializer.Deserialize(json, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);

        result.Should().BeEquivalentTo(contract);
    }

    private static CliResumableDownloadContract CreateContract() =>
        new()
        {
            FirstChunkIndex = 2,
            LastChunkIndex = 5,
            EncryptedPayload = "AQIDBA==",
            RequestedRange = new()
            {
                Start = 131072,
                End = 262144
            },
            TotalPlaintextSize = 1048576,
            ChunkSize = 65536,
            FinalChunkPlaintextLength = 32768
        };
}
