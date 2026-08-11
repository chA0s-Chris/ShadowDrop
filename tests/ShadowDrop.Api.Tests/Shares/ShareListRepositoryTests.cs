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
        var pending = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(active, now.AddHours(-4), now.AddDays(1), null), CancellationToken.None);
        await repository.CreateAsync(CreateShare(expired, now.AddHours(-3), now.AddDays(-1), null), CancellationToken.None);
        await repository.CreateAsync(CreateShare(revoked, now.AddHours(-2), now.AddDays(1), now.AddHours(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(pending, now.AddHours(-1), now.AddDays(-2), null), CancellationToken.None);
        await repository.TryRecordCleanupAttemptAsync(expired,
                                                      ShareCleanupState.Failed,
                                                      now,
                                                      [ShareCleanupFailureCategories.BlobDeleteFailed],
                                                      CancellationToken.None);

        var counts = await repository.GetStatusCountsAsync(now, CancellationToken.None);

        // The two surfaces must consume the same lifecycle predicates, so every status count has to equal the
        // share-list total for the equivalent single-status query evaluated at the same instant.
        async Task<Int64> TotalAsync(String status)
        {
            return await repository.CountMatchingAsync(new(now, [status], 1, null), CancellationToken.None);
        }

        counts.Active.Should().Be(await TotalAsync(ShareListStatuses.Active));
        counts.Expired.Should().Be(await TotalAsync(ShareListStatuses.Expired));
        counts.Revoked.Should().Be(await TotalAsync(ShareListStatuses.Revoked));
        counts.CleanupPending.Should().Be(await TotalAsync(ShareListStatuses.CleanupPending));
        counts.CleanupFailed.Should().Be(await TotalAsync(ShareListStatuses.CleanupFailed));
        // Only the unrevoked, unexpired share is active; the revoked one carries `revoked` instead.
        counts.Should().Be(new ShareStatusCounts(1, 2, 1, 3, 1));
    }

    [Test]
    public async Task LiteDb_ShouldBoundListPageQueries_ForEveryPageSizeAndCursorPosition()
    {
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");

        // Distinct creation timestamps are the shape the per-group walk degraded on: a 200-share page spanned about
        // 200 groups and issued one lookup for each. Equal timestamps are the opposite shape, where a continuation
        // has to resume inside a single group and the window boundary cuts another one in half.
        var tieTimestamps = new[]
        {
            now.AddHours(-1),
            now.AddHours(-2)
        };
        await AssertBoundedPagingAsync(index => now.AddSeconds(-index), 410, null);
        await AssertBoundedPagingAsync(index => tieTimestamps[index / 220],
                                       440,
                                       tieTimestamps.Select(timestamp => timestamp.ToUnixTimeMilliseconds()).ToArray());

        async Task AssertBoundedPagingAsync(Func<Int32, DateTimeOffset> createdAt, Int32 count, Int64[]? tieGroups)
        {
            await using var fixture = new RepositoryFixture();
            var limits = new List<Int32>();
            using var repository = new LiteDbShareMetadataRepository(fixture.Options, null, null, limits.Add);
            for (var index = 0; index < count; index++)
            {
                await repository.CreateAsync(CreateShare(Guid.NewGuid(), createdAt(index), now.AddDays(1), null),
                                             CancellationToken.None);
            }

            foreach (var pageSize in new[]
                     {
                         1,
                         50,
                         200
                     })
            {
                foreach (var statuses in new[]
                         {
                             Array.Empty<String>(),
                             new[] { ShareListStatuses.Active }
                         })
                {
                    ShareListCursor? cursor = null;
                    for (var page = 1; page <= 3; page++)
                    {
                        var because = $"page {page} of size {pageSize} with {statuses.Length} filter(s)";
                        if (page > 1)
                        {
                            cursor.Should().NotBeNull(because);

                            // Both seeded groups exceed the largest page, so on this shape every continuation
                            // resumes strictly inside a tie group - the position needing the cursor-group query.
                            if (tieGroups is not null)
                            {
                                cursor.CreatedAtUnixTimeMilliseconds.Should().BeOneOf(tieGroups, because);
                            }
                        }

                        limits.Clear();
                        var result = await repository.GetListPageAsync(new(now, statuses, pageSize, cursor),
                                                                       CancellationToken.None);

                        limits.Should().NotBeEmpty(because);
                        limits.Count.Should().BeLessThanOrEqualTo(4, because);
                        limits.Should().OnlyContain(limit => limit <= pageSize + 1, because);
                        result.Shares.Should().NotBeEmpty(because);
                        cursor = result.NextCursor;
                    }
                }
            }
        }
    }

    [Test]
    public async Task LiteDb_ShouldCompleteTruncatedTrailingGroup_AtWindowBoundary()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var newer = now.AddHours(-1);
        var older = now.AddHours(-2);

        // A page size of three reads a four-row window, so the two newer shares fill it only part way and the
        // five-share group behind them is cut in half by the window boundary on the very first page.
        var newerIds = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();
        var olderIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in newerIds.Concat(olderIds))
        {
            await repository.CreateAsync(CreateShare(id, newerIds.Contains(id) ? newer : older, now.AddDays(1), null),
                                         CancellationToken.None);
        }

        var expected = newerIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal)
                               .Concat(olderIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal))
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

        // The window returns an arbitrary subset of the group it truncates, so those rows must be discarded and
        // re-read in identifier order; otherwise the boundary silently drops, duplicates, or reorders a share.
        (await WalkAsync([])).Should().Equal(expected);
        (await WalkAsync([ShareListStatuses.Active])).Should().Equal(expected);
    }

    [Test]
    public async Task LiteDb_ShouldContinuePaging_WhenTheWholeWindowDisappearsBetweenQueries()
    {
        await using var fixture = new RepositoryFixture();
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var doomed = now.AddHours(-1);
        var survivors = now.AddHours(-2);
        var doomedIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var survivorIds = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();
        var queries = 0;
        LiteDbShareMetadataRepository? subject = null;

        void DeleteWindowBeforeTrailingRead(Int32 limit)
        {
            queries++;
            if (queries != 2 || subject is null)
            {
                return;
            }

            foreach (var id in doomedIds)
            {
                subject.TryDeleteAsync(id, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        using var repository = new LiteDbShareMetadataRepository(fixture.Options,
                                                                 null,
                                                                 null,
                                                                 DeleteWindowBeforeTrailingRead);
        subject = repository;

        foreach (var id in doomedIds)
        {
            await repository.CreateAsync(CreateShare(id, doomed, now.AddDays(1), null), CancellationToken.None);
        }

        foreach (var id in survivorIds)
        {
            await repository.CreateAsync(CreateShare(id, survivors, now.AddDays(1), null), CancellationToken.None);
        }

        // The four-row window is nothing but the newest tie group, and that whole group is deleted before its
        // re-read. There is no returned share to continue from, so the older group stays reachable only if the
        // window is read again below the vanished timestamp.
        var page = await repository.GetListPageAsync(new(now, [], 3, null), CancellationToken.None);
        page.Shares.Select(share => share.ShareId)
            .Should().Equal(survivorIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal));
        page.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task LiteDb_ShouldContinuePaging_WhenTrailingGroupDisappearsBetweenQueries()
    {
        await using var fixture = new RepositoryFixture();
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var newest = now.AddHours(-1);
        var doomed = now.AddHours(-2);
        var oldest = now.AddHours(-3);
        var newestIds = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();
        var doomedIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var oldestIds = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();
        var queries = 0;
        LiteDbShareMetadataRepository? subject = null;

        // The hook runs immediately before each provider query, so deleting the trailing group before its re-read
        // reproduces exactly the interleaving a concurrent cleanup run produces, without any timing dependence.
        void DeleteDoomedBeforeTrailingRead(Int32 limit)
        {
            queries++;
            if (queries != 2 || subject is null)
            {
                return;
            }

            foreach (var id in doomedIds)
            {
                subject.TryDeleteAsync(id, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        using var repository = new LiteDbShareMetadataRepository(fixture.Options,
                                                                 null,
                                                                 null,
                                                                 DeleteDoomedBeforeTrailingRead);
        subject = repository;

        foreach (var id in newestIds)
        {
            await repository.CreateAsync(CreateShare(id, newest, now.AddDays(1), null), CancellationToken.None);
        }

        foreach (var id in doomedIds)
        {
            await repository.CreateAsync(CreateShare(id, doomed, now.AddDays(1), null), CancellationToken.None);
        }

        foreach (var id in oldestIds)
        {
            await repository.CreateAsync(CreateShare(id, oldest, now.AddDays(1), null), CancellationToken.None);
        }

        // The four-row window ends inside the middle group, which vanishes before its re-read: the page comes back
        // short, but the oldest group is still there and must stay reachable.
        var page = await repository.GetListPageAsync(new(now, [], 3, null), CancellationToken.None);
        page.Shares.Select(share => share.ShareId)
            .Should().Equal(newestIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal));
        page.NextCursor.Should().NotBeNull();

        var next = await repository.GetListPageAsync(new(now, [], 3, page.NextCursor), CancellationToken.None);
        next.Shares.Select(share => share.ShareId)
            .Should().Equal(oldestIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal));
        next.NextCursor.Should().BeNull();
    }

    [Test]
    public async Task LiteDb_ShouldHideExpiredAndRevokedSharesOnlyFromTokenLookup()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var active = CreateShare(Guid.NewGuid(), now.AddHours(-3), now.AddHours(1), null);
        var expired = CreateShare(Guid.NewGuid(), now.AddHours(-2), now, null);
        var revoked = CreateShare(Guid.NewGuid(), now.AddHours(-1), now.AddHours(1), now.AddMinutes(-1));
        await repository.CreateAsync(active, CancellationToken.None);
        await repository.CreateAsync(expired, CancellationToken.None);
        await repository.CreateAsync(revoked, CancellationToken.None);

        (await repository.GetByShareTokenHashAsync(active.ShareTokenHashBase64, now, CancellationToken.None)).Should().NotBeNull();
        (await repository.GetByShareTokenHashAsync(expired.ShareTokenHashBase64, now, CancellationToken.None)).Should().BeNull();
        (await repository.GetByShareTokenHashAsync(revoked.ShareTokenHashBase64, now, CancellationToken.None)).Should().BeNull();
        (await repository.GetAsync(expired.ShareId, CancellationToken.None)).Should().NotBeNull();
        (await repository.GetAsync(revoked.ShareId, CancellationToken.None)).Should().NotBeNull();
        (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None)).Select(share => share.ShareId)
                                                                                 .Should().BeEquivalentTo([expired.ShareId, revoked.ShareId]);
    }

    [Test]
    public async Task LiteDb_ShouldOrFiltersWithoutDoubleCounting_AndReplaceCleanupFailureDetails()
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
                                                      ShareCleanupState.Failed,
                                                      retry,
                                                      [ShareCleanupFailureCategories.Unknown],
                                                      CancellationToken.None);
        var retried = await repository.GetAsync(expiredAndRevoked.ShareId, CancellationToken.None);
        retried!.CleanupState.Should().Be(ShareCleanupState.Failed);
        retried.LastCleanupAttemptAtUtc.Should().Be(retry);
        retried.CleanupFailureCategories.Should().Equal(ShareCleanupFailureCategories.Unknown);
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
            var document = collection.FindById(new(shareId));
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
    public async Task LiteDb_ShouldReadLegacyCompletedStateAsPendingCleanupCandidate()
    {
        await using var fixture = new RepositoryFixture();
        using var repository = new LiteDbShareMetadataRepository(fixture.Options);
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var share = CreateShare(Guid.NewGuid(), now.AddDays(-2), now.AddDays(-1), null);
        await repository.CreateAsync(share, CancellationToken.None);
        using (var database = new LiteDatabase(new ConnectionString
               {
                   Filename = fixture.Options.Metadata.LiteDbPath,
                   Connection = ConnectionType.Shared
               }))
        {
            var collection = database.GetCollection("shares");
            var document = collection.FindById(share.ShareId);
            document["CleanupState"] = "COMPLETED";
            collection.Update(document).Should().BeTrue();
        }

        (await repository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Pending);
        (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None)).Select(candidate => candidate.ShareId)
                                                                                 .Should().Contain(share.ShareId);
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
