// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using LiteDB;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Contracts;

public sealed class ShareListRepositoryTests
{
    [Test]
    public async Task LiteDb_ShouldAgreeBetweenStatusCounts_AndListTotalsForOneNowUtc()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var active = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        var completed = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(active, now.AddHours(-4), now.AddDays(1), null), CancellationToken.None);
        await repository.CreateAsync(CreateShare(expired, now.AddHours(-3), now.AddDays(-1), null), CancellationToken.None);
        await repository.CreateAsync(CreateShare(revoked, now.AddHours(-2), now.AddDays(1), now.AddHours(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(completed, now.AddHours(-1), now.AddDays(-2), null), CancellationToken.None);
        await repository.TryRecordCleanupAttemptAsync(completed, ShareCleanupState.Completed, now, [], CancellationToken.None);
        await repository.TryRecordCleanupAttemptAsync(expired,
                                                      ShareCleanupState.Failed,
                                                      now,
                                                      [ShareCleanupFailureCategories.BlobDeleteFailed],
                                                      CancellationToken.None);

        var counts = await repository.GetStatusCountsAsync(now, CancellationToken.None);

        // The two surfaces must consume the same lifecycle predicates, so every status count has to equal the
        // share-list total for the equivalent single-status query evaluated at the same instant.
        async Task<Int64> TotalAsync(String status) =>
            await repository.CountMatchingAsync(new(now, [status], 1, null), CancellationToken.None);

        counts.Active.Should().Be(await TotalAsync(ShareListStatuses.Active));
        counts.Expired.Should().Be(await TotalAsync(ShareListStatuses.Expired));
        counts.Revoked.Should().Be(await TotalAsync(ShareListStatuses.Revoked));
        counts.CleanupPending.Should().Be(await TotalAsync(ShareListStatuses.CleanupPending));
        counts.CleanupFailed.Should().Be(await TotalAsync(ShareListStatuses.CleanupFailed));
        counts.CleanupCompleted.Should().Be(await TotalAsync(ShareListStatuses.CleanupCompleted));
        // Only the unrevoked, unexpired share is active; the revoked one carries `revoked` instead.
        counts.Should().Be(new ShareStatusCounts(1, 2, 1, 2, 1, 1));
    }

    [Test]
    public async Task LiteDb_ShouldOrFiltersWithoutDoubleCounting_AndReplaceCleanupOutcome()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var expiredAndRevoked = CreateShare(Guid.NewGuid(), now.AddDays(-2), now.AddDays(-1), now.AddHours(-2));
        var revoked = CreateShare(Guid.NewGuid(), now.AddDays(-1), now.AddDays(1), now.AddHours(-1));
        await repository.CreateAsync(expiredAndRevoked, CancellationToken.None);
        await repository.CreateAsync(revoked, CancellationToken.None);

        var query = new ShareListQuery(now, [ShareListStatuses.Expired, ShareListStatuses.Revoked], 50, null);
        var page = await repository.GetListPageAsync(query, CancellationToken.None);

        page.Shares.Should().HaveCount(2);
        (await repository.CountMatchingAsync(query, CancellationToken.None)).Should().Be(2);

        var firstAttempt = now.AddMinutes(1);
        await repository.TryRecordCleanupAttemptAsync(expiredAndRevoked.ShareId,
                                                      ShareCleanupState.Failed,
                                                      firstAttempt,
                                                      [
                                                          ShareCleanupFailureCategories.Unknown,
                                                          ShareCleanupFailureCategories.BlobDeleteFailed,
                                                          ShareCleanupFailureCategories.Unknown
                                                      ],
                                                      CancellationToken.None);
        var failed = await repository.GetAsync(expiredAndRevoked.ShareId, CancellationToken.None);
        failed!.LastCleanupAttemptAtUtc.Should().Be(firstAttempt);
        failed.CleanupFailureCategories.Should().Equal(ShareCleanupFailureCategories.BlobDeleteFailed,
                                                       ShareCleanupFailureCategories.Unknown);

        var retry = now.AddMinutes(2);
        await repository.TryRecordCleanupAttemptAsync(expiredAndRevoked.ShareId,
                                                      ShareCleanupState.Completed,
                                                      retry,
                                                      [ShareCleanupFailureCategories.Unknown],
                                                      CancellationToken.None);
        var completed = await repository.GetAsync(expiredAndRevoked.ShareId, CancellationToken.None);
        completed!.CleanupState.Should().Be(ShareCleanupState.Completed);
        completed.LastCleanupAttemptAtUtc.Should().Be(retry);
        completed.CleanupFailureCategories.Should().BeEmpty();
    }

    [Test]
    public async Task LiteDb_ShouldPageThroughLargeEqualTimestampGroup_WithoutGapsOrDuplicates()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var shared = now.AddHours(-1);

        // Seven shares collide on a single millisecond, with one share either side on its own timestamp, so a
        // page size of three has to cross into, run through, and back out of the collision group.
        var collisionIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in collisionIds)
        {
            await repository.CreateAsync(CreateShare(id, shared, now.AddDays(1), null), CancellationToken.None);
        }

        var newest = Guid.NewGuid();
        var oldest = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(newest, shared.AddMinutes(1), now.AddDays(1), null), CancellationToken.None);
        await repository.CreateAsync(CreateShare(oldest, shared.AddMinutes(-1), now.AddDays(1), null), CancellationToken.None);

        var expected = new[] { newest }
                       .Concat(collisionIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal))
                       .Append(oldest)
                       .ToArray();

        async Task<IReadOnlyList<Guid>> WalkAsync(String[] statuses)
        {
            var walked = new List<Guid>();
            ShareListCursor? cursor = null;
            do
            {
                var page = await repository.GetListPageAsync(new(now, statuses, 3, cursor), CancellationToken.None);
                walked.AddRange(page.Shares.Select(share => share.ShareId));
                cursor = page.NextCursor;
            } while (cursor is not null);

            return walked;
        }

        // Unfiltered and filtered walks take different LiteDB query plans, so both have to agree with the
        // canonical (CreatedAtUtc, ShareId) descending order.
        (await WalkAsync([])).Should().Equal(expected);
        (await WalkAsync([ShareListStatuses.Active])).Should().Equal(expected);
        (await repository.CountMatchingAsync(new(now, [], 3, null), CancellationToken.None)).Should().Be(9);
        (await repository.CountMatchingAsync(new(now, [ShareListStatuses.Active], 3, null), CancellationToken.None)).Should().Be(9);
    }

    [Test]
    public async Task LiteDb_ShouldReadLegacyCleanupRecords_AsNullAttemptAndEmptyCategories()
    {
        await using var fixture = new RepositoryFixture();
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var shareId = Guid.NewGuid();
        using (var repository = new LiteDbShareMetadataRepository(fixture.Options))
        {
            await repository.CreateAsync(CreateShare(shareId, now.AddHours(-1), now.AddDays(1), null), CancellationToken.None);
            await repository.TryRecordCleanupAttemptAsync(shareId,
                                                          ShareCleanupState.Failed,
                                                          now,
                                                          [ShareCleanupFailureCategories.BlobDeleteFailed],
                                                          CancellationToken.None);
        }

        // Reduce the stored document to what a pre-#171 database holds: no attempt timestamp, no category list,
        // and a cleanup state this build does not recognize.
        using (var database = new LiteDatabase(new ConnectionString
               {
                   Filename = fixture.Options.Metadata.LiteDbPath,
                   Connection = ConnectionType.Shared
               }))
        {
            var collection = database.GetCollection("shares");
            var document = collection.FindById(new BsonValue(shareId));
            document.Remove("LastCleanupAttemptAtUnixTimeMilliseconds");
            document.Remove("CleanupFailureCategories");
            document["CleanupState"] = "ARCHIVED";
            collection.Update(document);
        }

        using var reopened = new LiteDbShareMetadataRepository(fixture.Options);
        var record = await reopened.GetAsync(shareId, CancellationToken.None);
        var page = await reopened.GetListPageAsync(new(now, [], 50, null), CancellationToken.None);
        var listed = page.Shares.Single(share => share.ShareId == shareId);

        record!.LastCleanupAttemptAtUtc.Should().BeNull();
        record.CleanupFailureCategories.Should().BeEmpty();
        record.CleanupState.Should().Be(ShareCleanupState.Pending);
        listed.LastCleanupAttemptAtUtc.Should().BeNull();
        listed.CleanupFailureCategories.Should().BeEmpty();
        listed.CleanupState.Should().Be(ShareCleanupState.Pending);
        (await reopened.CountMatchingAsync(new(now, [ShareListStatuses.CleanupPending], 50, null), CancellationToken.None))
            .Should().Be(1);
    }

    [Test]
    public async Task LiteDb_ShouldUseCanonicalGuidTieOrdering_AndStrictCursorContinuation()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var created = now.AddHours(-1);
        var ids = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        };
        foreach (var id in ids)
        {
            await repository.CreateAsync(CreateShare(id, created, now.AddDays(1), null), CancellationToken.None);
        }

        var first = await repository.GetListPageAsync(new(now, [], 2, null), CancellationToken.None);
        var second = await repository.GetListPageAsync(new(now, [], 2, first.NextCursor), CancellationToken.None);

        var expected = ids.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        first.Shares.Select(share => share.ShareId).Should().Equal(expected[..2]);
        second.Shares.Select(share => share.ShareId).Should().Equal(expected[2..]);
        first.NextCursor.Should().NotBeNull();
        second.NextCursor.Should().BeNull();
        (await repository.CountMatchingAsync(new(now, [], 2, first.NextCursor), CancellationToken.None)).Should().Be(3);
    }

    private static ShareRecord CreateShare(Guid id, DateTimeOffset created, DateTimeOffset expires, DateTimeOffset? revoked) =>
        new(id,
            $"token-{id:N}",
            created,
            expires,
            revoked,
            ShareCleanupState.Pending,
            false,
            null,
            [new(Guid.NewGuid(), "secret-name.bin", null)]);

    private sealed class RepositoryFixture : IAsyncDisposable
    {
        private readonly String _root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                     "artifacts",
                                                     "share-list-repository-tests",
                                                     Guid.NewGuid().ToString("N"));

        public RepositoryFixture()
        {
            Directory.CreateDirectory(_root);
            Options = new()
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(_root, "metadata", "shadowdrop.db")
                },
                Storage = new()
                {
                    LocalRoot = Path.Combine(_root, "storage")
                }
            };
        }

        public ShadowDropOptions Options { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
