// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Contracts;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;

public sealed class ShareListContractTests
{
    [Test]
    public void Cursor_ShouldRoundTripCanonicalQueryBinding_AndRejectVersionMismatch()
    {
        var cursor = new ShareListCursor(OperationalStatusProtocol.CurrentVersion,
                                         [ShareListStatuses.Active, ShareListStatuses.CleanupFailed],
                                         1_786_000_000_000,
                                         Guid.Parse("80000000-0000-0000-0000-000000000001"));

        ShareListCursor.TryDecode(cursor.Encode(), out var decoded).Should().BeTrue();

        decoded.Should().BeEquivalentTo(cursor);
        var futurePayload = cursor.Encode();
        var decodedBytes = Base64Url.DecodeFromChars(futurePayload);
        var changed = Encoding.UTF8.GetString(decodedBytes).Replace("1|", "2|", StringComparison.Ordinal);
        var future = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(changed));
        ShareListCursor.TryDecode(future, out _).Should().BeFalse();
    }

    [Test]
    public void SourceGeneratedMetadata_ShouldSerializeExactPageShape()
    {
        var page = new ShareListPageContract(
            1,
            [
                new("00000000-0000-0000-0000-000000000001",
                    DateTimeOffset.Parse("2026-08-03T10:00:00Z"),
                    DateTimeOffset.Parse("2026-08-04T10:00:00Z"),
                    null,
                    [ShareListStatuses.Active, ShareListStatuses.CleanupPending],
                    ShareListCleanupStates.Pending,
                    null,
                    [],
                    2,
                    42)
            ],
            null,
            1);

        var json = JsonSerializer.Serialize(page, OperationalStatusJsonSerializerContext.Default.ShareListPageContract);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            "protocolVersion", "items", "nextCursor", "totalMatching");
        document.RootElement.GetProperty("items")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "shareId", "createdAtUtc", "expiresAtUtc", "revokedAtUtc", "statuses", "cleanupState",
            "lastCleanupAttemptAtUtc", "cleanupFailureCategories", "fileCount", "ciphertextBytes");
    }
}
