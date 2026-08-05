// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using System.Net.Mime;

public sealed class UploadSweepServiceTests
{
    private static readonly DateTimeOffset CompletedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [Test]
    public async Task GetSweepCandidatesAsync_ShouldApplyEveryTieBreakerBeforeTheBatchLimit()
    {
        await using var fixture = new SweepFixture();
        var records = new List<(Guid FileId, DateTimeOffset CompletedAtUtc)>();
        for (var index = 0; index < UploadSweepService.MaxCandidatesPerRun + 25; index++)
        {
            var completedAt = CompletedAt.AddMilliseconds(UploadSweepService.MaxCandidatesPerRun + 25 - index);
            records.Add((await fixture.CompleteMetadataAsync(completedAt), completedAt));
        }

        var candidates = await fixture.Uploads.GetSweepCandidatesAsync(
            CompletedAt.AddDays(30), UploadSweepService.MaxCandidatesPerRun, CancellationToken.None);

        candidates.Select(candidate => candidate.FileId).Should().Equal(
            records.OrderBy(record => record.CompletedAtUtc)
                   .ThenBy(record => record.FileId)
                   .Take(UploadSweepService.MaxCandidatesPerRun)
                   .Select(record => record.FileId));
    }

    [Test]
    public async Task GetSweepCandidatesAsync_ShouldBackfillTheOrderKeyForLegacyLiteDbDocuments()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                "artifacts",
                                "upload-sweep-order-backfill",
                                Guid.NewGuid().ToString("N"));
        var options = new ShadowDropOptions
        {
            Metadata = new()
            {
                LiteDbPath = Path.Combine(root, "metadata", "shadowdrop.db")
            }
        };
        var fileId = Guid.NewGuid();
        Directory.CreateDirectory(Path.GetDirectoryName(options.Metadata.LiteDbPath)!);
        try
        {
            using (var database = new LiteDatabase(options.Metadata.LiteDbPath))
            {
                database.GetCollection("uploaded_files").Insert(new BsonDocument
                {
                    ["_id"] = fileId,
                    ["BlobKey"] = $"legacy/{fileId:N}",
                    ["IsReserved"] = false,
                    ["CompletedAtUnixTimeMilliseconds"] = CompletedAt.ToUnixTimeMilliseconds()
                });
            }

            using (var repository = new LiteDbUploadedFileMetadataRepository(
                       options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance))
            {
                var candidates = await repository.GetSweepCandidatesAsync(
                    CompletedAt.AddDays(1), UploadSweepService.MaxCandidatesPerRun, CancellationToken.None);
                candidates.Select(candidate => candidate.FileId).Should().Equal(fileId);
            }

            using var verification = new LiteDatabase(options.Metadata.LiteDbPath);
            verification.GetCollection("uploaded_files").FindById(fileId)["SweepOrderKey"].AsString.Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task GetSweepCandidatesAsync_ShouldPrioritizeNeverInspectedThenLeastRecentlyInspected()
    {
        await using var fixture = new SweepFixture();
        var neverInspected = await fixture.CompleteUploadAsync(CompletedAt);
        var recentlyInspected = await fixture.CompleteUploadAsync(CompletedAt);
        var longAgoInspected = await fixture.CompleteUploadAsync(CompletedAt);
        (await fixture.Uploads.TryRecordSweepInspectionAsync(recentlyInspected, CompletedAt.AddDays(2), CancellationToken.None))
            .Should().BeTrue();
        (await fixture.Uploads.TryRecordSweepInspectionAsync(longAgoInspected, CompletedAt.AddDays(1), CancellationToken.None))
            .Should().BeTrue();

        var candidates = await fixture.Uploads.GetSweepCandidatesAsync(CompletedAt.AddDays(30), 200, CancellationToken.None);

        candidates.Select(candidate => candidate.FileId).Should().Equal(neverInspected, longAgoInspected, recentlyInspected);
    }

    [Test]
    public async Task RunAsync_ShouldConvergeAfterMetadataDeletionFailure()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var uploads = new FailFirstMetadataDeleteRepository(fixture.Uploads);

        var first = await fixture.CreateSweep(CompletedAt + Retention, uploads: uploads).RunAsync(CancellationToken.None);
        var second = await fixture.CreateSweep(CompletedAt + Retention, uploads: uploads).RunAsync(CancellationToken.None);

        first.Should().Be(new UploadSweepResult(1, 0, 0, 1));

        // The blob is already gone by the retry, so the converged run must not claim it freed storage a second time.
        second.Should().Be(new UploadSweepResult(1, 0, 1, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldInspectAtMostFiftySweepClaimsPerRun_AndRotateThem()
    {
        await using var fixture = new SweepFixture();
        for (var index = 0; index < UploadSweepService.MaxRecoveryClaimsPerRun + 1; index++)
        {
            // No upload row was ever written for these files, which is exactly the state a crash between a
            // successful metadata deletion and the claim release leaves behind.
            (await fixture.Claims.TryAcquireAsync(Guid.NewGuid(),
                                                  ShareOperationClaimKind.SweepUpload,
                                                  Guid.NewGuid(),
                                                  [Guid.NewGuid()],
                                                  CancellationToken.None)).Should().NotBeNull();
        }

        _ = await fixture.CreateSweep(CompletedAt).RunAsync(CancellationToken.None);
        var afterFirstRun = await fixture.Claims.GetSweepClaimsAsync(1000, CancellationToken.None);
        _ = await fixture.CreateSweep(CompletedAt).RunAsync(CancellationToken.None);

        afterFirstRun.Should().ContainSingle("a run recovers at most 50 claims, separately from the candidate budget");
        (await fixture.Claims.GetSweepClaimsAsync(1000, CancellationToken.None)).Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_ShouldInspectAtMostTwoHundredCandidatesPerRun()
    {
        await using var fixture = new SweepFixture();
        for (var index = 0; index < UploadSweepService.MaxCandidatesPerRun + 1; index++)
        {
            _ = await fixture.CompleteMetadataAsync(CompletedAt);
        }

        var first = await fixture.CreateSweep(CompletedAt + Retention, new AlwaysMissingBlobStorage()).RunAsync(CancellationToken.None);
        var second = await fixture.CreateSweep(CompletedAt + Retention, new AlwaysMissingBlobStorage()).RunAsync(CancellationToken.None);

        first.CandidatesInspected.Should().Be(UploadSweepService.MaxCandidatesPerRun);
        second.CandidatesInspected.Should().Be(1);
        second.Failures.Should().Be(0);
    }

    [Test]
    public async Task RunAsync_ShouldLogOnlyTheFileIdentifier_WhenReclamationFails()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt, "top-secret-name.txt");
        var blobKey = (await fixture.Uploads.GetAsync(fileId, CancellationToken.None))!.BlobKey;
        var logger = new CapturingLogger<UploadSweepService>();

        var result = await fixture.CreateSweep(CompletedAt + Retention,
                                               new ThrowingBlobStorage(
                                                   new IOException($"blob store offline for {blobKey}/top-secret-name.txt")),
                                               logger: logger)
                                  .RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(message => message.Contains(blobKey) || message.Contains("top-secret-name.txt"));
        logger.Messages.Should().Contain(message => message.Contains(fileId.ToString()));
        logger.Exceptions.Should().OnlyContain(exception => exception == null,
                                               "provider exceptions can carry blob keys and paths");
    }

    [Test]
    public async Task RunAsync_ShouldNeverSweepReservations([Values] Boolean claimed)
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.Uploads.ReserveFileIdAsync(CancellationToken.None);
        if (claimed)
        {
            (await fixture.Uploads.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        }

        var result = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        result.Should().Be(new UploadSweepResult(0, 0, 0, 0));
        (await fixture.Uploads.GetActivePendingReservationCountAsync(CancellationToken.None)).Should().Be(claimed ? 0 : 1);
    }

    [Test]
    public async Task RunAsync_ShouldReclaimUnreferencedUpload_OnlyAtOrAfterTheGracePeriod(
        [Values(-1, 0, 1)] Int32 millisecondsPastCutoff)
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var blobKey = fixture.BlobKeyOf(fileId);
        var expectReclaimed = millisecondsPastCutoff >= 0;

        var result = await fixture.CreateSweep(CompletedAt + Retention + TimeSpan.FromMilliseconds(millisecondsPastCutoff))
                                  .RunAsync(CancellationToken.None);

        result.Should().Be(expectReclaimed ? new UploadSweepResult(1, 1, 0, 0) : new UploadSweepResult(0, 0, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None) is null).Should().Be(expectReclaimed);
        fixture.BlobExists(blobKey).Should().Be(!expectReclaimed);
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldReclaim_AfterAbortingAnAbandonedAcquiredCreationClaim()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        (await fixture.Claims.TryAcquireAsync(Guid.NewGuid(),
                                              ShareOperationClaimKind.CreateShare,
                                              Guid.NewGuid(),
                                              [fileId],
                                              CancellationToken.None)).Should().NotBeNull();

        var result = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        result.Should().Be(new UploadSweepResult(1, 1, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldReleaseOrphanedSweepClaim_WhenItsUploadMetadataIsAlreadyGone()
    {
        await using var fixture = new SweepFixture();
        var orphanedFileId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        (await fixture.Claims.TryAcquireAsync(operationId,
                                              ShareOperationClaimKind.SweepUpload,
                                              operationId,
                                              [orphanedFileId],
                                              CancellationToken.None)).Should().NotBeNull();

        var result = await fixture.CreateSweep(CompletedAt).RunAsync(CancellationToken.None);

        result.Failures.Should().Be(0);
        (await fixture.Claims.GetSweepClaimsAsync(1000, CancellationToken.None)).Should().BeEmpty();
        await fixture.AssertFileIsUnclaimedAsync(orphanedFileId);
    }

    [Test]
    public async Task RunAsync_ShouldRetainClaimAndMetadata_WhenRetainedBlobAccountingFails()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var uploads = new FailFirstAccountingRepository(fixture.Uploads);

        var first = await fixture.CreateSweep(CompletedAt + Retention, uploads: uploads).RunAsync(CancellationToken.None);
        await fixture.AssertFileIsClaimedAsync(fileId);
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        var second = await fixture.CreateSweep(CompletedAt + Retention, uploads: uploads).RunAsync(CancellationToken.None);

        first.Failures.Should().Be(1);
        second.Failures.Should().Be(0);
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldRetainClaimAndMetadata_WhenTheBlobDeletionFails()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var blobKey = fixture.BlobKeyOf(fileId);

        var first = await fixture.CreateSweep(CompletedAt + Retention, new ThrowingBlobStorage(new IOException("offline")))
                                 .RunAsync(CancellationToken.None);
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        fixture.BlobExists(blobKey).Should().BeTrue();
        await fixture.AssertFileIsClaimedAsync(fileId);

        // The retained claim is reacquired idempotently because the sweep derives its operation identifier from
        // the file identifier, so the retry converges instead of deadlocking on its own leftovers.
        var second = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        first.Should().Be(new UploadSweepResult(1, 0, 0, 1));
        second.Should().Be(new UploadSweepResult(1, 1, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldRotateSkippedAndFailingCandidates_SoFreshOnesAreNotStarved()
    {
        await using var fixture = new SweepFixture();
        var referenced = await fixture.CompleteUploadAsync(CompletedAt);
        var failing = await fixture.CompleteUploadAsync(CompletedAt);
        await fixture.Shares.CreateAsync(CreateShareRecord(referenced, CompletedAt.AddDays(30)), CancellationToken.None);
        var now = CompletedAt + Retention;

        var result = await fixture.CreateSweep(now, new FailingBlobStorage(fixture.BlobKeyOf(failing), fixture.Blobs)).RunAsync(CancellationToken.None);
        var fresh = await fixture.CompleteMetadataAsync(CompletedAt);
        var candidates = await fixture.Uploads.GetSweepCandidatesAsync(now, 200, CancellationToken.None);

        result.CandidatesInspected.Should().Be(2);
        result.Failures.Should().Be(1);

        // Both the skipped and the failing record were stamped, so a never-inspected upload now sorts ahead of
        // them and a permanently failing record cannot hold the front of the queue. The two stamped records
        // share an inspection timestamp and fall back to the file-identifier tie-breaker.
        candidates.Select(candidate => candidate.FileId).Should().Equal([
            fresh, .. new[]
            {
                referenced,
                failing
            }.Order()
        ]);
    }

    [Test]
    public async Task RunAsync_ShouldSkipCandidate_WhenAClaimConflictRemainsUnresolved()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var blobKey = fixture.BlobKeyOf(fileId);

        // A cleanup claim is not a share creation, so reconciliation cannot resolve it. The sweep must defer the
        // file to a later run rather than doing destructive work or reporting a failure.
        (await fixture.Claims.TryAcquireAsync(Guid.NewGuid(),
                                              ShareOperationClaimKind.CleanupShare,
                                              Guid.NewGuid(),
                                              [fileId],
                                              CancellationToken.None)).Should().NotBeNull();

        var result = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        result.Should().Be(new UploadSweepResult(1, 0, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        fixture.BlobExists(blobKey).Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_ShouldSkipReferencedUpload_AndReleaseItsClaim(
        [Values("active", "expired", "revoked")]
        String lifecycle)
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var blobKey = fixture.BlobKeyOf(fileId);
        var share = CreateShareRecord(fileId, lifecycle == "expired" ? CompletedAt.AddDays(1) : CompletedAt.AddDays(30));
        await fixture.Shares.CreateAsync(share, CancellationToken.None);
        if (lifecycle == "revoked")
        {
            (await fixture.Shares.TryRevokeAsync(share.ShareId, CompletedAt.AddDays(1), CancellationToken.None)).Should().BeTrue();
        }

        var result = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        result.Should().Be(new UploadSweepResult(1, 0, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        fixture.BlobExists(blobKey).Should().BeTrue();
        await fixture.AssertFileIsUnclaimedAsync(fileId);
    }

    [Test]
    public async Task RunAsync_ShouldSkipUpload_WhenAnAbandonedCommittingCreationClaimStillOwesItsShare()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        var blobKey = fixture.BlobKeyOf(fileId);
        var share = CreateShareRecord(fileId, CompletedAt.AddDays(30));
        var operationId = Guid.NewGuid();
        (await fixture.Claims.TryAcquireAsync(operationId,
                                              ShareOperationClaimKind.CreateShare,
                                              share.ShareId,
                                              [fileId],
                                              CancellationToken.None)).Should().NotBeNull();
        (await fixture.Claims.TryBeginCommitAsync(operationId, share, CancellationToken.None)).Should().BeTrue();

        var result = await fixture.CreateSweep(CompletedAt + Retention).RunAsync(CancellationToken.None);

        // Reconciliation finishes the abandoned creation, which makes the file referenced; the sweep must then
        // leave it alone rather than deleting ciphertext a freshly recovered share points at.
        result.Should().Be(new UploadSweepResult(1, 0, 0, 0));
        (await fixture.Shares.GetAsync(share.ShareId, CancellationToken.None)).Should().NotBeNull();
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        fixture.BlobExists(blobKey).Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_ShouldStampLegacyCompletion_AndWaitAFullGracePeriodFromThere()
    {
        await using var fixture = new SweepFixture();
        var fileId = await fixture.CompleteUploadAsync(CompletedAt);
        fixture.ClearCompletionTimestamp(fileId);
        var stampedAt = CompletedAt.AddDays(90);

        var stamping = await fixture.CreateSweep(stampedAt).RunAsync(CancellationToken.None);
        var justBefore = await fixture.CreateSweep(stampedAt + Retention - TimeSpan.FromMilliseconds(1)).RunAsync(CancellationToken.None);
        var eligible = await fixture.CreateSweep(stampedAt + Retention).RunAsync(CancellationToken.None);

        // The legacy record is inspected and stamped, then waits a full grace period from that stamp — never
        // reclaimed on the first encounter merely because it carried no timestamp.
        stamping.Should().Be(new UploadSweepResult(1, 0, 0, 0));
        justBefore.Should().Be(new UploadSweepResult(0, 0, 0, 0));
        eligible.Should().Be(new UploadSweepResult(1, 1, 0, 0));
        (await fixture.Uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldReportSweepCountersAndTreatASweepOnlyFailureAsPartial()
    {
        await using var fixture = new SweepFixture();
        var reclaimed = await fixture.CompleteUploadAsync(CompletedAt);
        var failing = await fixture.CompleteUploadAsync(CompletedAt);
        var missingBlob = await fixture.CompleteMetadataAsync(CompletedAt);
        var now = CompletedAt + Retention;
        var timeProvider = new FrozenTimeProvider(now);
        var cleanupService = new ShareCleanupService(fixture.Shares,
                                                     fixture.Uploads,
                                                     fixture.Claims,
                                                     fixture.Blobs,
                                                     timeProvider,
                                                     NullLogger<ShareCleanupService>.Instance);
        var status = new CleanupRunStatus();
        using var coordinator = new InProcessShareCleanupCoordinator();
        var runner = new ShareCleanupRunner(cleanupService,
                                            fixture.CreateSweep(now, new FailingBlobStorage(fixture.BlobKeyOf(failing), fixture.Blobs)),
                                            coordinator,
                                            timeProvider,
                                            status,
                                            NullLogger<ShareCleanupRunner>.Instance);

        var result = await runner.RunIfIdleAsync(CancellationToken.None);

        result.SweepCandidatesInspected.Should().Be(3);
        result.SweepUploadsDeleted.Should().Be(1);
        result.SweepBlobsAlreadyMissing.Should().Be(1);
        result.SweepFailures.Should().Be(1);

        // The share phase found nothing to do, so the run total is exactly the sweep's failure and the run is
        // still reported as a partial failure.
        result.Failures.Should().Be(1);
        status.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now, CleanupRunStatus.PartialFailure));
        (await fixture.Uploads.GetAsync(reclaimed, CancellationToken.None)).Should().BeNull();
        (await fixture.Uploads.GetAsync(missingBlob, CancellationToken.None)).Should().BeNull();
        (await fixture.Uploads.GetAsync(failing, CancellationToken.None)).Should().NotBeNull();
    }

    private static ShareRecord CreateShareRecord(Guid fileId, DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(),
            $"share-token-hash-{Guid.NewGuid():N}",
            CompletedAt,
            expiresAtUtc,
            null,
            ShareCleanupState.Pending,
            false,
            null,
            [new(fileId, "cipher.bin", null)]);

    private sealed class AlwaysMissingBlobStorage : IBlobStorage
    {
        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Exception?> Exceptions { get; } = [];

        public List<String> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public Boolean IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, String> formatter)
        {
            Exceptions.Add(exception);
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class FailFirstAccountingRepository(IUploadedFileMetadataRepository inner) : UploadedFileRepositoryDecorator(inner)
    {
        private Int32 _calls;

        public override Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _calls) == 1
                ? Task.FromResult(false)
                : base.TryMarkBlobDeletedAsync(fileId, cancellationToken);
    }

    private sealed class FailFirstMetadataDeleteRepository(IUploadedFileMetadataRepository inner) : UploadedFileRepositoryDecorator(inner)
    {
        private Int32 _calls;

        public override Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _calls) == 1
                ? Task.FromResult(false)
                : base.TryDeleteAsync(fileId, cancellationToken);
    }

    /// <summary>Fails the blob deletion of exactly one file, leaving every other candidate to the real store.</summary>
    private sealed class FailingBlobStorage(String failingBlobKey, IBlobStorage inner) : IBlobStorage
    {
        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) =>
            String.Equals(blobKey, failingBlobKey, StringComparison.Ordinal)
                ? throw new IOException("blob store offline")
                : inner.DeleteIfExistsAsync(blobKey, cancellationToken);

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FrozenTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class SweepFixture : IAsyncDisposable
    {
        private readonly String _root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                     "artifacts",
                                                     "upload-sweep-tests",
                                                     Guid.NewGuid().ToString("N"));

        public SweepFixture()
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
                },
                Cleanup = new()
                {
                    UnreferencedUploadRetention = Retention
                }
            };
            Uploads = new(Options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
            Shares = new(Options);
            Claims = new(Options);
            Blobs = new LocalBlobStorage(Options, NullLogger<LocalBlobStorage>.Instance);
        }

        public IBlobStorage Blobs { get; }

        public LiteDbShareOperationClaimRepository Claims { get; }

        public ShadowDropOptions Options { get; }

        public LiteDbShareMetadataRepository Shares { get; }

        public LiteDbUploadedFileMetadataRepository Uploads { get; }

        public async Task AssertFileIsClaimedAsync(Guid fileId) =>
            (await Claims.TryAcquireAsync(Guid.NewGuid(),
                                          ShareOperationClaimKind.CreateShare,
                                          Guid.NewGuid(),
                                          [fileId],
                                          CancellationToken.None))
            .Should().BeNull("the sweep must keep its claim so no share creation can adopt the file");

        public async Task AssertFileIsUnclaimedAsync(Guid fileId)
        {
            var probeOperationId = Guid.NewGuid();
            (await Claims.TryAcquireAsync(probeOperationId,
                                          ShareOperationClaimKind.CreateShare,
                                          Guid.NewGuid(),
                                          [fileId],
                                          CancellationToken.None))
                .Should().NotBeNull("no sweep claim may outlive a completed or skipped candidate");
            (await Claims.TryReleaseAsync(probeOperationId, CancellationToken.None)).Should().BeTrue();
        }

        public Boolean BlobExists(String blobKey) => File.Exists(Path.Combine(Options.Storage.LocalRoot, blobKey));

        public String BlobKeyOf(Guid fileId)
        {
            using var database = new LiteDatabase(Options.Metadata.LiteDbPath);
            return database.GetCollection("uploaded_files").FindById(fileId)["BlobKey"].AsString;
        }

        /// <summary>Reproduces a record written before completion timestamps existed.</summary>
        public void ClearCompletionTimestamp(Guid fileId) => SetCompletionTimestamp(fileId, null);

        /// <summary>Records a completed upload whose ciphertext was never written, so its blob is already missing.</summary>
        public async Task<Guid> CompleteMetadataAsync(DateTimeOffset completedAtUtc)
        {
            var fileId = await Uploads.ReserveFileIdAsync(CancellationToken.None);
            (await Uploads.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
            (await Uploads.TryCompleteReservationAsync(CreateRecord(fileId, $"metadata/{fileId:N}.blob", "cipher.bin"),
                                                       CancellationToken.None)).Should().BeTrue();
            SetCompletionTimestamp(fileId, completedAtUtc);
            return fileId;
        }

        public async Task<Guid> CompleteUploadAsync(DateTimeOffset completedAtUtc, String originalFileName = "cipher.bin")
        {
            var fileId = await Uploads.ReserveFileIdAsync(CancellationToken.None);
            (await Uploads.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
            var descriptor = await Blobs.SaveAsync(fileId, new MemoryStream([1, 2, 3, 4]), CancellationToken.None);
            (await Uploads.TryCompleteReservationAsync(CreateRecord(fileId, descriptor.BlobKey, originalFileName),
                                                       CancellationToken.None)).Should().BeTrue();
            SetCompletionTimestamp(fileId, completedAtUtc);
            return fileId;
        }

        public UploadSweepService CreateSweep(
            DateTimeOffset nowUtc,
            IBlobStorage? blobStorage = null,
            IUploadedFileMetadataRepository? uploads = null,
            ILogger<UploadSweepService>? logger = null)
        {
            var effectiveUploads = uploads ?? Uploads;
            return new(effectiveUploads,
                       Shares,
                       Claims,
                       new(Claims, Shares, NullLogger<ShareCreationClaimReconciler>.Instance),
                       blobStorage ?? Blobs,
                       Options,
                       new FrozenTimeProvider(nowUtc),
                       logger ?? NullLogger<UploadSweepService>.Instance);
        }

        private static UploadedFileRecord CreateRecord(Guid fileId, String blobKey, String originalFileName) =>
            new(fileId,
                blobKey,
                originalFileName,
                4,
                4,
                MediaTypeNames.Application.Octet,
                "1",
                "AES-256-GCM",
                4,
                1,
                Convert.ToBase64String([1, 2, 3, 4]),
                new('a', 64));

        private void SetCompletionTimestamp(Guid fileId, DateTimeOffset? completedAtUtc)
        {
            using var database = new LiteDatabase(Options.Metadata.LiteDbPath);
            var collection = database.GetCollection("uploaded_files");
            var document = collection.FindById(fileId);
            ((Object?)document).Should().NotBeNull();
            document!["CompletedAtUnixTimeMilliseconds"] = completedAtUtc is { } completedAt
                ? completedAt.ToUnixTimeMilliseconds()
                : BsonValue.Null;
            document["SweepOrderKey"] = LiteDbUploadedFileMetadataRepository.CreateSweepOrderKey(
                null, completedAtUtc?.ToUnixTimeMilliseconds(), fileId);
            collection.Update(document);
        }

        public ValueTask DisposeAsync()
        {
            Uploads.Dispose();
            Shares.Dispose();
            Claims.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingBlobStorage(Exception failure) : IBlobStorage
    {
        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => throw failure;

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Forwards everything to a real repository so only the failure under test differs from production.</summary>
    private abstract class UploadedFileRepositoryDecorator(IUploadedFileMetadataRepository inner) : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) =>
            inner.GetActivePendingReservationCountAsync(cancellationToken);

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            inner.GetAsync(fileId, cancellationToken);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) =>
            inner.GetStorageStatsAsync(cancellationToken);

        public Task<IReadOnlyList<UploadSweepCandidate>> GetSweepCandidatesAsync(
            DateTimeOffset completionCutoffUtc,
            Int32 limit,
            CancellationToken cancellationToken) =>
            inner.GetSweepCandidatesAsync(completionCutoffUtc, limit, cancellationToken);

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) =>
            inner.ReleaseClaimAsync(fileId, cancellationToken);

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => inner.ReserveFileIdAsync(cancellationToken);

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) =>
            inner.TryClaimReservationAsync(fileId, cancellationToken);

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            inner.TryCompleteReservationAsync(record, cancellationToken);

        public virtual Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken) =>
            inner.TryDeleteAsync(fileId, cancellationToken);

        public virtual Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) =>
            inner.TryMarkBlobDeletedAsync(fileId, cancellationToken);

        public Task<Boolean> TryRecordSweepInspectionAsync(
            Guid fileId,
            DateTimeOffset inspectedAtUtc,
            CancellationToken cancellationToken) =>
            inner.TryRecordSweepInspectionAsync(fileId, inspectedAtUtc, cancellationToken);
    }
}
