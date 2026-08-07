// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Text.Json;

public sealed class ShareInspectionContractTests
{
    [Test]
    public void SourceGeneratedMetadata_ShouldRoundTripExactInspectionShape()
    {
        var inspection = new ShareInspectionContract(
            1,
            "80000000-0000-0000-0000-000000000001",
            DateTimeOffset.Parse("2026-08-07T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"),
            null,
            [ShareListStatuses.Active, ShareListStatuses.CleanupPending],
            ShareListCleanupStates.Pending,
            null,
            [],
            1,
            42,
            [new("a0000000-0000-0000-0000-000000000001", 42, ShareFileRetentionStates.Retained, null, null)]);

        var typeInfo = OperationalStatusJsonSerializerContext.Default.ShareInspectionContract;
        var json = JsonSerializer.Serialize(inspection, typeInfo);
        var roundTrip = JsonSerializer.Deserialize(json, typeInfo);

        roundTrip.Should().BeEquivalentTo(inspection);
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            "protocolVersion", "shareId", "createdAtUtc", "expiresAtUtc", "revokedAtUtc", "statuses", "cleanupState",
            "lastCleanupAttemptAtUtc", "cleanupFailureCategories", "fileCount", "ciphertextBytes", "files");
        document.RootElement.GetProperty("files")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "fileId", "ciphertextBytes", "retentionState", "originalFilename", "displayName");
        json.Should().Contain("\"createdAtUtc\":\"2026-08-07T10:00:00+00:00\"")
            .And.Contain("\"originalFilename\":null")
            .And.Contain("\"displayName\":null");
    }
}
