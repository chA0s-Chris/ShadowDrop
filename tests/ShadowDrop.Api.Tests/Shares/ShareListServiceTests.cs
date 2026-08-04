// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;

public sealed class ShareListServiceTests
{
    [TestCase(1)]
    [TestCase(50)]
    [TestCase(200)]
    public async Task GetAsync_ShouldAcceptDocumentedPageSizes(Int32 pageSize)
    {
        var shares = new RecordingShareRepository(new([], null), 0);
        var service = new ShareListService(shares, new RecordingFileRepository([]), TimeProvider.System);

        _ = await service.GetAsync(StringValues.Empty, new(pageSize.ToString()), StringValues.Empty, CancellationToken.None);

        shares.Query!.PageSize.Should().Be(pageSize);
    }

    [Test]
    public async Task GetAsync_ShouldDefaultPageSize_AndBindCursorOnlyToNormalizedFilters()
    {
        var shares = new RecordingShareRepository(new([], null), 0);
        var service = new ShareListService(shares, new RecordingFileRepository([]), TimeProvider.System);
        var cursor = new ShareListCursor(OperationalStatusProtocol.CurrentVersion,
                                         [ShareListStatuses.Active],
                                         DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                         Guid.NewGuid()).Encode();

        _ = await service.GetAsync(new(ShareListStatuses.Active), StringValues.Empty, new(cursor), CancellationToken.None);
        var mismatch = () => service.GetAsync(new(ShareListStatuses.Expired), new("200"), new(cursor), CancellationToken.None);

        shares.Query!.PageSize.Should().Be(ShareListPagination.DefaultPageSize);
        (await mismatch.Should().ThrowAsync<ShareListValidationException>()).Which.Reason.Should().Be(OperationalErrorReasons.InvalidCursor);
    }

    [Test]
    public async Task GetAsync_ShouldNormalizeFilters_AndAggregateRetainedCiphertextOnce()
    {
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00Z");
        var retainedId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var share = new ShareListRecord(Guid.Parse("80000000-0000-0000-0000-000000000001"),
                                        now.AddHours(-1),
                                        now,
                                        now.AddMinutes(-2),
                                        ShareCleanupState.Failed,
                                        now.AddMinutes(-1),
                                        [
                                            ShareCleanupFailureCategories.Unknown, ShareCleanupFailureCategories.BlobDeleteFailed,
                                            ShareCleanupFailureCategories.Unknown
                                        ],
                                        [retainedId, deletedId]);
        var shares = new RecordingShareRepository(new([share], null), 1);
        var files = new RecordingFileRepository([
            new(retainedId, 100, BlobRetentionState.Retained),
            new(deletedId, 900, BlobRetentionState.Deleted)
        ]);
        var time = new CountingTimeProvider(now);
        var service = new ShareListService(shares, files, time);

        var page = await service.GetAsync(new([
                                              ShareListStatuses.CleanupFailed, ShareListStatuses.Revoked,
                                              ShareListStatuses.CleanupFailed
                                          ]),
                                          new("1"),
                                          StringValues.Empty,
                                          CancellationToken.None);

        time.ReadCount.Should().Be(1);
        shares.Query!.Statuses.Should().Equal(ShareListStatuses.Revoked, ShareListStatuses.CleanupFailed);
        files.Calls.Should().Be(1);
        page.TotalMatching.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].Statuses.Should().Equal(ShareListStatuses.Expired, ShareListStatuses.Revoked, ShareListStatuses.CleanupFailed);
        page.Items[0].CleanupFailureCategories.Should().Equal(ShareCleanupFailureCategories.BlobDeleteFailed,
                                                              ShareCleanupFailureCategories.Unknown);
        page.Items[0].FileCount.Should().Be(2);
        page.Items[0].CiphertextBytes.Should().Be(100);
    }

    [Test]
    public async Task GetAsync_ShouldValidateRequestBeforeCursor_AndFailOnMissingBatchMetadata()
    {
        var service = new ShareListService(new RecordingShareRepository(new([], null), 0),
                                           new RecordingFileRepository([]),
                                           new CountingTimeProvider(DateTimeOffset.Parse("2026-08-03T12:00:00Z")));

        var invalid = () => service.GetAsync(new("unknown"), new("0"), new("bad"), CancellationToken.None);
        var tooLarge = () => service.GetAsync(StringValues.Empty, new("201"), StringValues.Empty, CancellationToken.None);

        (await invalid.Should().ThrowAsync<ShareListValidationException>()).Which.Reason.Should().Be(OperationalErrorReasons.InvalidRequest);
        (await tooLarge.Should().ThrowAsync<ShareListValidationException>()).Which.Reason.Should().Be(OperationalErrorReasons.InvalidRequest);

        var fileId = Guid.NewGuid();
        var share = new ShareListRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), null,
                                        ShareCleanupState.Pending, null, [], [fileId]);
        var incomplete = new ShareListService(new RecordingShareRepository(new([share], null), 1),
                                              new RecordingFileRepository([]),
                                              TimeProvider.System);
        var act = () => incomplete.GetAsync(StringValues.Empty, StringValues.Empty, StringValues.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public Int32 ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            ReadCount++;
            return now;
        }
    }

    private sealed class RecordingFileRepository(IReadOnlyList<UploadedFileListProjection> projections) : IUploadedFileMetadataRepository
    {
        public Int32 Calls { get; private set; }

        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UploadedFileListProjection>> GetListProjectionsAsync(IReadOnlyCollection<Guid> fileIds,
                                                                                       CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(projections);
        }

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingShareRepository(ShareListRepositoryPage page, Int64 total) : IShareMetadataRepository
    {
        public ShareListQuery? Query { get; private set; }

        public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken)
        {
            Query.Should().Be(query);
            return Task.FromResult(total);
        }

        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(page);
        }

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryRecordCleanupAttemptAsync(Guid shareId, ShareCleanupState cleanupState, DateTimeOffset completedAtUtc,
                                                          IReadOnlyCollection<String> failureCategories,
                                                          CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
