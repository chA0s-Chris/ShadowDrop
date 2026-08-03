// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Text.Json;

public sealed class OperationalStatusContractTests
{
    [Test]
    public void SourceGeneratedMetadata_ShouldRoundTripProtocolVersionOneContracts()
    {
        var status = new AdminServerStatusContract(
            OperationalStatusProtocol.CurrentVersion,
            true,
            true,
            OperationalStatusReasons.None,
            new(true, true, true, true),
            "1.2.3",
            42,
            [new("metadata", OperationalComponentStates.Ready, OperationalStatusReasons.None)],
            new("litedb", "filesystem"),
            new(2, 100),
            new(1, 2, 3, 4, 5, 6),
            new(DateTimeOffset.Parse("2026-08-02T12:00:00Z"), "success"),
            new(null),
            []);

        var json = JsonSerializer.Serialize(status, OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract);
        var roundTripped = JsonSerializer.Deserialize(json, OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract);

        roundTripped.Should().BeEquivalentTo(status);
        json.Should().Contain("\"protocolVersion\":1").And.NotContain("ProtocolVersion");
    }
}
