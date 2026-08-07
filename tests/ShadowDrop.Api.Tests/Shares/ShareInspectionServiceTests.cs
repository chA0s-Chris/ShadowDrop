// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;

public sealed class ShareInspectionServiceTests
{
    [Test]
    public async Task GetAsync_ShouldMapEveryRetentionState_PreserveOrder_AndRedactByDefault()
    {
        var now = DateTimeOffset.Parse("2026-08-07T10:00:00Z");
        var retained = Guid.Parse("A0000000-0000-0000-0000-000000000001");
        var deleted = Guid.Parse("A0000000-0000-0000-0000-000000000002");
        var unknown = Guid.Parse("A0000000-0000-0000-0000-000000000003");
        var missing = Guid.Parse("A0000000-0000-0000-0000-000000000004");
        var share = Share([
            new(retained, "retained.txt", "retained display"),
            new(deleted, "deleted.txt", null),
            new(unknown, "unknown.txt", "unknown display"),
            new(missing, "missing.txt", "missing display")
        ]);
        var shares = new InspectionShareRepository(share);
        var files = new InspectionFileRepository([
            new(unknown, 300, BlobRetentionState.Unknown),
            new(retained, 100, BlobRetentionState.Retained),
            new(deleted, 200, BlobRetentionState.Deleted)
        ]);
        var time = new CountingTimeProvider(now);
        var service = new ShareInspectionService(shares, files, time);

        var inspection = await service.GetAsync(share.ShareId, false, CancellationToken.None);

        inspection.Should().NotBeNull();
        time.ReadCount.Should().Be(1);
        shares.RequestedShareId.Should().Be(share.ShareId);
        files.Calls.Should().Be(1);
        files.RequestedIds.Should().BeEquivalentTo([retained, deleted, unknown, missing]);
        inspection.ShareId.Should().Be("80000000-0000-0000-0000-000000000001");
        inspection.Statuses.Should().Equal(ShareListStatuses.Active, ShareListStatuses.CleanupPending);
        inspection.FileCount.Should().Be(4);
        inspection.CiphertextBytes.Should().Be(100);
        inspection.Files.Select(file => file.FileId).Should().Equal(
            retained.ToString("D").ToLowerInvariant(),
            deleted.ToString("D").ToLowerInvariant(),
            unknown.ToString("D").ToLowerInvariant(),
            missing.ToString("D").ToLowerInvariant());
        inspection.Files.Select(file => file.RetentionState).Should().Equal(
            ShareFileRetentionStates.Retained,
            ShareFileRetentionStates.Deleted,
            ShareFileRetentionStates.Unknown,
            ShareFileRetentionStates.Missing);
        inspection.Files.Select(file => file.CiphertextBytes).Should().Equal(100, 0, 0, 0);
        inspection.Files.Should().OnlyContain(file => file.OriginalFilename == null && file.DisplayName == null);
    }

    [Test]
    public async Task GetAsync_ShouldPopulateFilenamesOnlyAfterExplicitOptIn()
    {
        var fileId = Guid.NewGuid();
        var share = Share([new(fileId, "original.txt", "display.txt")]);
        var service = new ShareInspectionService(new InspectionShareRepository(share),
                                                 new InspectionFileRepository([new(fileId, 42, BlobRetentionState.Retained)]),
                                                 TimeProvider.System);

        var inspection = await service.GetAsync(share.ShareId, true, CancellationToken.None);

        inspection.Should().NotBeNull();
        inspection.Files.Should().ContainSingle();
        inspection.Files[0].OriginalFilename.Should().Be("original.txt");
        inspection.Files[0].DisplayName.Should().Be("display.txt");
    }

    [Test]
    public async Task GetAsync_ShouldRejectDuplicateOrUnexpectedBatchProjections([Values] Boolean duplicate)
    {
        var fileId = Guid.NewGuid();
        var share = Share([new(fileId, "file.txt", null)]);
        IReadOnlyList<UploadedFileListProjection> projections = duplicate
            ? [new(fileId, 1, BlobRetentionState.Retained), new(fileId, 1, BlobRetentionState.Retained)]
            : [new(Guid.NewGuid(), 1, BlobRetentionState.Retained)];
        var service = new ShareInspectionService(new InspectionShareRepository(share),
                                                 new InspectionFileRepository(projections),
                                                 TimeProvider.System);

        var act = () => service.GetAsync(share.ShareId, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task GetAsync_ShouldReturnNullWithoutQueryingFiles_WhenShareDoesNotExist()
    {
        var files = new InspectionFileRepository([]);
        var service = new ShareInspectionService(new InspectionShareRepository(null), files, TimeProvider.System);

        var inspection = await service.GetAsync(Guid.NewGuid(), true, CancellationToken.None);

        inspection.Should().BeNull();
        files.Calls.Should().Be(0);
    }

    private static ShareRecord Share(IReadOnlyList<ShareFileEntryRecord> files) =>
        new(Guid.Parse("80000000-0000-0000-0000-000000000001"),
            "sensitive-token-hash",
            DateTimeOffset.Parse("2026-08-07T09:00:00+02:00"),
            DateTimeOffset.Parse("2026-08-08T09:00:00+02:00"),
            null,
            ShareCleanupState.Pending,
            false,
            null,
            files,
            Guid.NewGuid(),
            null,
            []);

    private sealed class CountingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public CountingTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public Int32 ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            ReadCount++;
            return _now;
        }
    }

    private sealed class InspectionFileRepository : IUploadedFileMetadataRepository
    {
        private readonly IReadOnlyList<UploadedFileListProjection> _projections;

        public InspectionFileRepository(IReadOnlyList<UploadedFileListProjection> projections)
        {
            _projections = projections;
        }

        public Int32 Calls { get; private set; }
        public IReadOnlyCollection<Guid> RequestedIds { get; private set; } = [];

        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UploadedFileListProjection>> GetListProjectionsAsync(
            IReadOnlyCollection<Guid> fileIds,
            CancellationToken cancellationToken)
        {
            Calls++;
            RequestedIds = fileIds;
            return Task.FromResult(_projections);
        }

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InspectionShareRepository : IShareMetadataRepository
    {
        private readonly ShareRecord? _share;

        public InspectionShareRepository(ShareRecord? share)
        {
            _share = share;
        }

        public Guid? RequestedShareId { get; private set; }

        public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken)
        {
            RequestedShareId = shareId;
            return Task.FromResult(_share);
        }

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, DateTimeOffset nowUtc,
                                                           CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc,
                                                                          CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRecordCleanupAttemptAsync(Guid shareId, ShareCleanupState cleanupState,
                                                          DateTimeOffset completedAtUtc,
                                                          IReadOnlyCollection<String> failureCategories,
                                                          CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
