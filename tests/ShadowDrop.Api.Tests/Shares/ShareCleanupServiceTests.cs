// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net.Mime;

public sealed class ShareCleanupServiceTests
{
    [Test]
    public async Task ExecuteAsync_ShouldRunCleanupAtStartupAndThenOnSchedule()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-28T00:30:00Z"));
        var shareRepository = new SignalingShareRepository();
        var cleanupService = new ShareCleanupService(shareRepository,
                                                     new InMemoryUploadedFileRepository(CreateUploadedFileRecord(Guid.NewGuid())),
                                                     new InMemoryShareOperationClaimRepository(),
                                                     new BlockingBlobStorage(),
                                                     timeProvider,
                                                     NullLogger<ShareCleanupService>.Instance);
        using var coordinator = new InProcessShareCleanupCoordinator();
        var runner = new ShareCleanupRunner(cleanupService,
                                            CreateIdleSweepService(),
                                            coordinator,
                                            NullLogger<ShareCleanupRunner>.Instance);
        var options = new ShadowDropOptions();
        using var hostedService = new ShareCleanupHostedService(runner, options, timeProvider, NullLogger<ShareCleanupHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);

        (await shareRepository.CleanupScanned.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("cleanup should run once shortly after startup");

        await WaitForScheduledTimerAsync(timeProvider);
        timeProvider.Advance(TimeSpan.FromHours(2));

        (await shareRepository.CleanupScanned.WaitAsync(TimeSpan.FromSeconds(5))).Should()
                                                                                 .BeTrue("cleanup should run again after the scheduled interval elapses");

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task RunAsync_ShouldCleanupRevokedShare_EvenWhenItHasNotExpired()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-10T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        (await shareRepository.TryRevokeAsync(share.ShareId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"), CancellationToken.None))
            .Should().BeTrue();
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
        (await uploadedFileRepository.GetAsync(uploadedFile.FileId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldConvergeAfterPartialUploadedMetadataDeletion()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var firstFileId = Guid.NewGuid();
        var secondFileId = Guid.NewGuid();
        var share = new ShareRecord(Guid.NewGuid(),
                                    $"token-{Guid.NewGuid():N}",
                                    now.AddDays(-2),
                                    now.AddDays(-1),
                                    null,
                                    ShareCleanupState.Pending,
                                    false,
                                    null,
                                    [new(firstFileId, "first.bin", null), new(secondFileId, "second.bin", null)]);
        var shares = new InMemoryShareRepository(share);
        var uploads = new FailSecondMetadataDeleteUploadedFileRepository(
            [CreateUploadedFileRecord(firstFileId), CreateUploadedFileRecord(secondFileId)]);
        var service = new ShareCleanupService(shares,
                                              uploads,
                                              new InMemoryShareOperationClaimRepository(),
                                              new AlwaysMissingBlobStorage(),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var first = await service.RunAsync(CancellationToken.None);
        (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
        uploads.RemainingFileIds.Should().Equal(secondFileId);
        var second = await service.RunAsync(CancellationToken.None);

        first.Failures.Should().Be(1);
        second.Failures.Should().Be(0);
        uploads.RemainingFileIds.Should().BeEmpty();
        (await shares.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldConvergeWhenFinalShareDeletionInitiallyFails()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shares = new InMemoryShareRepository(share, true);
        var uploads = new FailSecondMetadataDeleteUploadedFileRepository([CreateUploadedFileRecord(fileId)]);
        var service = new ShareCleanupService(shares,
                                              uploads,
                                              new InMemoryShareOperationClaimRepository(),
                                              new AlwaysMissingBlobStorage(),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var first = await service.RunAsync(CancellationToken.None);
        (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
        uploads.RemainingFileIds.Should().BeEmpty();
        var second = await service.RunAsync(CancellationToken.None);

        first.Failures.Should().Be(1);
        (await shares.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
        second.Failures.Should().Be(0);
    }

    [Test]
    public async Task RunAsync_ShouldDeleteBlobAndAllMetadataForExpiredShare()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        File.Exists(Path.Combine(options.Storage.LocalRoot, uploadedFile.BlobKey)).Should().BeFalse();
        (await uploadedFileRepository.GetAsync(uploadedFile.FileId, CancellationToken.None)).Should().BeNull();
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldLogCompletionAtInformation_WhenNoFailuresOccurred()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        await shareRepository.CreateAsync(CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z")), CancellationToken.None);
        var collector = new FakeLogCollector();
        var sut = CreateService(shareRepository,
                                uploadedFileRepository,
                                blobStorage,
                                DateTimeOffset.Parse("2026-06-02T00:00:00Z"),
                                new FakeLogger<ShareCleanupService>(collector));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(0);
        var completionRecord = collector.GetSnapshot().Single(logRecord => logRecord.Message.Contains("Share cleanup completed"));
        completionRecord.Level.Should().Be(LogLevel.Information);
        completionRecord.Message.Should().NotContain("with failures");
        completionRecord.StructuredState!.Should().Contain(pair => pair.Key == "Failures" && pair.Value == "0");
    }

    [Test]
    public async Task RunAsync_ShouldLogCompletionAtWarning_WhenFailuresOccurred()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);

        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        var failingShareRepository = new InMemoryShareRepository(share);
        var collector = new FakeLogCollector();
        var sut = new ShareCleanupService(failingShareRepository,
                                          new ThrowingReadUploadedFileRepository(new TimeoutException("metadata unavailable")),
                                          new InMemoryShareOperationClaimRepository(),
                                          blobStorage,
                                          new FrozenTimeProvider(DateTimeOffset.Parse("2026-06-02T00:00:00Z")),
                                          new FakeLogger<ShareCleanupService>(collector));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        var completionRecord = collector.GetSnapshot().Single(logRecord => logRecord.Message.Contains("Share cleanup completed with failures"));
        completionRecord.Level.Should().Be(LogLevel.Warning);
        completionRecord.StructuredState!.Should().Contain(pair => pair.Key == "Failures" && pair.Value == "1");
    }

    [Test]
    public async Task RunAsync_ShouldNotLogSensitiveShareMaterial()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        const String secretMaterial = "SUPER-SECRET-SHARE-MATERIAL";
        var share = new ShareRecord(Guid.NewGuid(),
                                    secretMaterial,
                                    DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                    null,
                                    ShareCleanupState.Pending,
                                    false,
                                    new(secretMaterial, DateTimeOffset.Parse("2026-06-01T00:00:00Z")),
                                    [new(uploadedFile.FileId, "cipher.bin", null)]);
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var logger = new CapturingLogger<ShareCleanupService>();
        var sut = new ShareCleanupService(shareRepository,
                                          uploadedFileRepository,
                                          new InMemoryShareOperationClaimRepository(),
                                          blobStorage,
                                          new FrozenTimeProvider(DateTimeOffset.Parse("2026-06-02T00:00:00Z")),
                                          logger);

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(message => message.Contains(secretMaterial));
    }

    [Test]
    public async Task RunAsync_ShouldPerformNoDestructiveWork_WhenCleanupClaimConflicts()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shares = new InMemoryShareRepository(share);
        var uploads = new InMemoryUploadedFileRepository(CreateUploadedFileRecord(fileId));
        var claims = new InMemoryShareOperationClaimRepository();
        (await claims.TryAcquireAsync(Guid.NewGuid(),
                                      ShareOperationClaimKind.CreateShare,
                                      Guid.NewGuid(),
                                      [fileId],
                                      CancellationToken.None)).Should().NotBeNull();
        var blobs = new CountingBlobStorage();
        var service = new ShareCleanupService(shares,
                                              uploads,
                                              claims,
                                              blobs,
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var result = await service.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        blobs.DeleteCalls.Should().Be(0);
        (await uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
        (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
    }

    [Test]
    public async Task RunAsync_ShouldRecordBlobDeleteFailure_AndSkipRetainedAccounting()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shareRepository = new InMemoryShareRepository(share);
        var uploadRepository = new FlakyAccountingUploadedFileRepository(CreateUploadedFileRecord(fileId));
        var service = new ShareCleanupService(shareRepository,
                                              uploadRepository,
                                              new InMemoryShareOperationClaimRepository(),
                                              new ThrowingBlobStorage(new IOException("blob store offline")),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var result = await service.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        var failed = await shareRepository.GetAsync(share.ShareId, CancellationToken.None);
        failed!.CleanupState.Should().Be(ShareCleanupState.Failed);
        failed.CleanupFailureCategories.Should().Equal(ShareCleanupFailureCategories.BlobDeleteFailed);

        // The blob is still there, so retained-blob accounting must not be marked as deleted.
        uploadRepository.TransitionCalls.Should().Be(0);
    }

    [Test]
    public async Task RunAsync_ShouldRecordMetadataUnavailable_WhenRetainedAccountingRejectsTheTransition()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shareRepository = new InMemoryShareRepository(share);
        var service = new ShareCleanupService(shareRepository,
                                              new FlakyAccountingUploadedFileRepository(CreateUploadedFileRecord(fileId)),
                                              new InMemoryShareOperationClaimRepository(),
                                              new SequenceDeleteBlobStorage(),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var result = await service.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        var failed = await shareRepository.GetAsync(share.ShareId, CancellationToken.None);
        failed!.CleanupState.Should().Be(ShareCleanupState.Failed);
        failed.CleanupFailureCategories.Should().Equal(ShareCleanupFailureCategories.MetadataUnavailable);
    }

    [TestCase(ShareOperationClaimLifecycle.Committing, true)]
    [TestCase(ShareOperationClaimLifecycle.Acquired, false)]
    public async Task RunAsync_ShouldReleaseAbandonedCreationClaim_OnlyWhenItAlreadyCommitted(
        ShareOperationClaimLifecycle lifecycle,
        Boolean expectPurge)
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shares = new InMemoryShareRepository(share);
        var uploads = new FailSecondMetadataDeleteUploadedFileRepository([CreateUploadedFileRecord(fileId)]);
        var claims = new InMemoryShareOperationClaimRepository();

        // A share-creation claim whose owner died before releasing it. Only the committing one proves the
        // creation finished, because the share record is inserted after that transition wins; an acquired
        // claim could still belong to an owner about to insert, so it must keep blocking cleanup.
        var operationId = Guid.NewGuid();
        (await claims.TryAcquireAsync(operationId,
                                      ShareOperationClaimKind.CreateShare,
                                      share.ShareId,
                                      [fileId],
                                      CancellationToken.None)).Should().NotBeNull();
        if (lifecycle == ShareOperationClaimLifecycle.Committing)
        {
            (await claims.TryBeginCommitAsync(operationId, share, CancellationToken.None)).Should().BeTrue();
        }

        var service = new ShareCleanupService(shares,
                                              uploads,
                                              claims,
                                              new AlwaysMissingBlobStorage(),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var result = await service.RunAsync(CancellationToken.None);

        if (expectPurge)
        {
            result.Failures.Should().Be(0);
            result.SharesCompleted.Should().Be(1);
            uploads.RemainingFileIds.Should().BeEmpty();
            (await shares.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
        }
        else
        {
            result.Failures.Should().Be(1);
            uploads.RemainingFileIds.Should().Equal(fileId);
            (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
        }
    }

    [Test]
    public async Task RunAsync_ShouldRetainEveryUploadRowAndCleanupClaim_WhenLaterBlobFails()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploads = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shares = new LiteDbShareMetadataRepository(options);
        using var claims = new LiteDbShareOperationClaimRepository(options);
        var firstFileId = await CompleteMetadataAsync(uploads);
        var secondFileId = await CompleteMetadataAsync(uploads);
        var share = new ShareRecord(Guid.NewGuid(),
                                    $"token-{Guid.NewGuid():N}",
                                    DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                    null,
                                    ShareCleanupState.Pending,
                                    false,
                                    null,
                                    [new(firstFileId, "first.bin", null), new(secondFileId, "second.bin", null)]);
        await shares.CreateAsync(share, CancellationToken.None);
        var sut = new ShareCleanupService(shares,
                                          uploads,
                                          claims,
                                          new FailSecondDeleteBlobStorage(),
                                          new FrozenTimeProvider(DateTimeOffset.Parse("2026-06-02T00:00:00Z")),
                                          NullLogger<ShareCleanupService>.Instance);

        var result = await sut.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        (await uploads.GetAsync(firstFileId, CancellationToken.None)).Should().NotBeNull();
        (await uploads.GetAsync(secondFileId, CancellationToken.None)).Should().NotBeNull();
        (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
        (await claims.TryAcquireAsync(Guid.NewGuid(),
                                      ShareOperationClaimKind.CreateShare,
                                      Guid.NewGuid(),
                                      [firstFileId, secondFileId],
                                      CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldRetryAfterRetainedAccountingTransitionFailure()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shareRepository = new InMemoryShareRepository(share);
        var uploadRepository = new FlakyAccountingUploadedFileRepository(CreateUploadedFileRecord(fileId));
        var blobStorage = new SequenceDeleteBlobStorage();
        var service = new ShareCleanupService(shareRepository,
                                              uploadRepository,
                                              new InMemoryShareOperationClaimRepository(),
                                              blobStorage,
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var first = await service.RunAsync(CancellationToken.None);
        var second = await service.RunAsync(CancellationToken.None);

        first.Failures.Should().Be(1);
        second.Should().Be(new ShareCleanupResult(1, 1, 0, 1, 0));
        uploadRepository.TransitionCalls.Should().Be(2);
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldSeparateExpectedProviderOutagesFromUnclassifiableFailures(
        [Values] Boolean unclassifiable)
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var fileId = Guid.NewGuid();
        var share = CreateShareRecord(fileId, now.AddDays(-1));
        var shareRepository = new InMemoryShareRepository(share);

        // A timeout is what an unreachable metadata provider looks like; a NullReferenceException is a defect and
        // must not be reported to the operator as a provider outage.
        Exception failure = unclassifiable ? new NullReferenceException("defect") : new TimeoutException("provider offline");
        var service = new ShareCleanupService(shareRepository,
                                              new ThrowingReadUploadedFileRepository(failure),
                                              new InMemoryShareOperationClaimRepository(),
                                              new SequenceDeleteBlobStorage(),
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);

        var result = await service.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        var failed = await shareRepository.GetAsync(share.ShareId, CancellationToken.None);
        failed!.CleanupState.Should().Be(ShareCleanupState.Failed);
        failed.CleanupFailureCategories.Should().Equal(unclassifiable
                                                           ? ShareCleanupFailureCategories.Unknown
                                                           : ShareCleanupFailureCategories.MetadataUnavailable);
    }

    [Test]
    public async Task RunAsync_ShouldStopStartingNewFilesAfterCleanupLeaseLoss()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var firstFileId = Guid.NewGuid();
        var secondFileId = Guid.NewGuid();
        var share = new ShareRecord(Guid.NewGuid(),
                                    $"token-{Guid.NewGuid():N}",
                                    now.AddDays(-2),
                                    now.AddDays(-1),
                                    null,
                                    ShareCleanupState.Pending,
                                    false,
                                    null,
                                    [new(firstFileId, "first.bin", null), new(secondFileId, "second.bin", null)]);
        var shares = new InMemoryShareRepository(share);
        var uploads = new FailSecondMetadataDeleteUploadedFileRepository(
            [CreateUploadedFileRecord(firstFileId), CreateUploadedFileRecord(secondFileId)]);
        var blobs = new CountingBlobStorage();
        var service = new ShareCleanupService(shares,
                                              uploads,
                                              new InMemoryShareOperationClaimRepository(),
                                              blobs,
                                              new FrozenTimeProvider(now),
                                              NullLogger<ShareCleanupService>.Instance);
        var checks = 0;

        var result = await service.RunAsync(() => Interlocked.Increment(ref checks) <= 2, CancellationToken.None);

        // Losing the lease is an orderly hand-off, not a failure: nothing is deleted beyond the file already
        // started, and the share stays pending so a later run — here or on another instance — converges.
        result.Failures.Should().Be(0);
        result.SharesCompleted.Should().Be(0);
        blobs.DeleteCalls.Should().Be(1);
        uploads.RemainingFileIds.Should().BeEquivalentTo([firstFileId, secondFileId]);
        (await shares.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Pending);
    }

    [Test]
    public async Task RunAsync_ShouldTreatAlreadyMissingUploadMetadataAsSuccess()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var share = CreateShareRecord(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var firstResult = await sut.RunAsync(CancellationToken.None);
        var secondResult = await sut.RunAsync(CancellationToken.None);

        firstResult.Should().Be(new ShareCleanupResult(1, 1, 0, 1, 0));
        secondResult.Should().Be(new ShareCleanupResult(0, 0, 0, 0, 0));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunAsync_ShouldTreatMissingBlobAsCompletedAndRemainIdempotent()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        File.Delete(Path.Combine(options.Storage.LocalRoot, uploadedFile.BlobKey));
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var firstResult = await sut.RunAsync(CancellationToken.None);
        var secondResult = await sut.RunAsync(CancellationToken.None);

        firstResult.Should().Be(new ShareCleanupResult(1, 1, 0, 1, 0));
        secondResult.Should().Be(new ShareCleanupResult(0, 0, 0, 0, 0));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldLogStartedEvent_AndNotSkipped_WhenIdle()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var cleanupService = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));
        var collector = new FakeLogCollector();
        using var coordinator = new InProcessShareCleanupCoordinator();
        var sut = new ShareCleanupRunner(cleanupService,
                                         CreateIdleSweepService(),
                                         coordinator,
                                         new FakeLogger<ShareCleanupRunner>(collector));

        var result = await sut.RunIfIdleAsync(CancellationToken.None);

        result.Skipped.Should().BeFalse();
        var logRecords = collector.GetSnapshot();
        logRecords.Should().ContainSingle();
        logRecords[0].Level.Should().Be(LogLevel.Information);
        logRecords[0].Message.Should().Contain("Share cleanup started");
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldSkip_WhenCleanupIsAlreadyRunning()
    {
        var fileId = Guid.NewGuid();
        var shareRepository = new InMemoryShareRepository(CreateShareRecord(fileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z")));
        var uploadRepository = new InMemoryUploadedFileRepository(new(fileId,
                                                                      "blob-key",
                                                                      "cipher.bin",
                                                                      1,
                                                                      1,
                                                                      MediaTypeNames.Application.Octet,
                                                                      "1",
                                                                      "AES-256-GCM",
                                                                      1,
                                                                      1,
                                                                      "salt",
                                                                      "sha"));
        var blobStorage = new BlockingBlobStorage();
        var cleanupService = CreateService(shareRepository, uploadRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));
        var collector = new FakeLogCollector();
        using var coordinator = new InProcessShareCleanupCoordinator();
        var runner = new ShareCleanupRunner(cleanupService,
                                            CreateIdleSweepService(),
                                            coordinator,
                                            new FakeLogger<ShareCleanupRunner>(collector));

        var firstRun = runner.RunIfIdleAsync(CancellationToken.None);
        await blobStorage.DeleteStarted.Task;
        var secondRun = await runner.RunIfIdleAsync(CancellationToken.None);
        blobStorage.AllowDeleteToFinish.SetResult();
        var firstResult = await firstRun;

        secondRun.Skipped.Should().BeTrue();
        firstResult.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));

        var logRecords = collector.GetSnapshot();
        logRecords.Should().ContainSingle(logRecord => logRecord.Message.Contains("Share cleanup started"))
                  .Which.Level.Should().Be(LogLevel.Information);
        logRecords.Should().ContainSingle(logRecord => logRecord.Message.Contains("Share cleanup skipped"))
                  .Which.Level.Should().Be(LogLevel.Information);
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldTrackFailure_WhenLeaseAcquisitionFails()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var timeProvider = new FrozenTimeProvider(now);
        var status = new CleanupRunStatus();
        var runner = new ShareCleanupRunner(CreateEmptyCleanupService(timeProvider),
                                            CreateIdleSweepService(),
                                            new ThrowingAcquireCoordinator(),
                                            timeProvider,
                                            status,
                                            NullLogger<ShareCleanupRunner>.Instance);

        var act = async () => await runner.RunIfIdleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        status.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now, CleanupRunStatus.Failure));
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldTrackFailure_WhenLeaseDisposalFails()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var timeProvider = new FrozenTimeProvider(now);
        var status = new CleanupRunStatus();
        var runner = new ShareCleanupRunner(CreateEmptyCleanupService(timeProvider),
                                            CreateIdleSweepService(),
                                            new ThrowingDisposeCoordinator(),
                                            timeProvider,
                                            status,
                                            NullLogger<ShareCleanupRunner>.Instance);

        var act = async () => await runner.RunIfIdleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        status.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now, CleanupRunStatus.Failure));
    }

    [Test]
    public async Task RunIfIdleAsync_ShouldTrackSuccessSkippedAndPreserveOutcomeOnCancellation()
    {
        var now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");
        var timeProvider = new FrozenTimeProvider(now);
        var fileId = Guid.NewGuid();
        var shareRepository = new InMemoryShareRepository(CreateShareRecord(fileId, now.AddDays(-1)));
        var uploadRepository = new InMemoryUploadedFileRepository(CreateUploadedFileRecord(fileId));
        var completedStorage = new BlockingBlobStorage();
        completedStorage.AllowDeleteToFinish.SetResult();
        var cleanupService = new ShareCleanupService(shareRepository,
                                                     uploadRepository,
                                                     new InMemoryShareOperationClaimRepository(),
                                                     completedStorage,
                                                     timeProvider,
                                                     NullLogger<ShareCleanupService>.Instance);
        var status = new CleanupRunStatus();
        using var coordinator = new InProcessShareCleanupCoordinator();
        var runner = new ShareCleanupRunner(cleanupService,
                                            CreateIdleSweepService(),
                                            coordinator,
                                            timeProvider,
                                            status,
                                            NullLogger<ShareCleanupRunner>.Instance);

        _ = await runner.RunIfIdleAsync(CancellationToken.None);

        status.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now, CleanupRunStatus.Success));

        var skippedStatus = new CleanupRunStatus();
        var skippedRunner = new ShareCleanupRunner(cleanupService,
                                                   CreateIdleSweepService(),
                                                   new NeverAcquireCoordinator(),
                                                   timeProvider,
                                                   skippedStatus,
                                                   NullLogger<ShareCleanupRunner>.Instance);
        (await skippedRunner.RunIfIdleAsync(CancellationToken.None)).Skipped.Should().BeTrue();
        skippedStatus.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now, CleanupRunStatus.Skipped));

        var cancellationStatus = new CleanupRunStatus();
        cancellationStatus.Record(now.AddMinutes(-1), CleanupRunStatus.Success);
        var blockingStorage = new BlockingBlobStorage();
        var cancelledShare = CreateShareRecord(fileId, now.AddDays(-1));
        var cancelledShareRepository = new InMemoryShareRepository(cancelledShare);
        var cancellationService = new ShareCleanupService(
            cancelledShareRepository,
            uploadRepository,
            new InMemoryShareOperationClaimRepository(),
            blockingStorage,
            timeProvider,
            NullLogger<ShareCleanupService>.Instance);
        using var cancellationCoordinator = new InProcessShareCleanupCoordinator();
        var cancellationRunner = new ShareCleanupRunner(cancellationService,
                                                        CreateIdleSweepService(),
                                                        cancellationCoordinator,
                                                        timeProvider,
                                                        cancellationStatus,
                                                        NullLogger<ShareCleanupRunner>.Instance);
        using var cancellation = new CancellationTokenSource();
        var run = cancellationRunner.RunIfIdleAsync(cancellation.Token);
        await blockingStorage.DeleteStarted.Task;
        await cancellation.CancelAsync();

        var act = async () => await run;
        await act.Should().ThrowAsync<OperationCanceledException>();
        cancellationStatus.Snapshot.Should().Be(new CleanupRunStatusSnapshot(now.AddMinutes(-1), CleanupRunStatus.Success));

        // Cancellation must record no attempt at all: the outcome fields stay exactly as the share was created, so a
        // refactor that moved the attempt write outside the cancellable path cannot slip through unnoticed.
        var cancelled = await cancelledShareRepository.GetAsync(cancelledShare.ShareId, CancellationToken.None);
        cancelled!.CleanupState.Should().Be(ShareCleanupState.Pending);
        cancelled.LastCleanupAttemptAtUtc.Should().BeNull();
        cancelled.CleanupFailureCategories.Should().BeEmpty();
    }

    private static async Task<Guid> CompleteMetadataAsync(IUploadedFileMetadataRepository repository)
    {
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryCompleteReservationAsync(CreateUploadedFileRecord(fileId), CancellationToken.None)).Should().BeTrue();
        return fileId;
    }

    private static async Task<UploadedFileRecord> CompleteUploadAsync(IUploadedFileMetadataRepository repository, IBlobStorage blobStorage)
    {
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        var descriptor = await blobStorage.SaveAsync(fileId, new MemoryStream([1, 2, 3, 4]), CancellationToken.None);
        var record = new UploadedFileRecord(fileId,
                                            descriptor.BlobKey,
                                            "cipher.bin",
                                            4,
                                            descriptor.WrittenLength,
                                            MediaTypeNames.Application.Octet,
                                            "1",
                                            "AES-256-GCM",
                                            4,
                                            1,
                                            Convert.ToBase64String([1, 2, 3, 4]),
                                            new('a', 64));
        (await repository.TryCompleteReservationAsync(record, CancellationToken.None)).Should().BeTrue();
        return record;
    }

    private static ShareCleanupService CreateEmptyCleanupService(TimeProvider timeProvider) =>
        new(new SignalingShareRepository(),
            new InMemoryUploadedFileRepository(CreateUploadedFileRecord(Guid.NewGuid())),
            new InMemoryShareOperationClaimRepository(),
            new BlockingBlobStorage(),
            timeProvider,
            NullLogger<ShareCleanupService>.Instance);

    /// <summary>
    /// A sweep with nothing to reclaim. These tests are about the share phase, the run lease, and run status;
    /// <see cref="UploadSweepServiceTests"/> covers reclamation itself.
    /// </summary>
    private static UploadSweepService CreateIdleSweepService()
    {
        var shares = new SignalingShareRepository();
        var claims = new InMemoryShareOperationClaimRepository();
        return new(new NoSweepCandidatesUploadedFileRepository(),
                   shares,
                   claims,
                   new(claims, shares, NullLogger<ShareCreationClaimReconciler>.Instance),
                   new AlwaysMissingBlobStorage(),
                   new(),
                   TimeProvider.System,
                   NullLogger<UploadSweepService>.Instance);
    }

    private static ShareCleanupService CreateService(IShareMetadataRepository shareRepository,
                                                     IUploadedFileMetadataRepository uploadedFileRepository,
                                                     IBlobStorage blobStorage,
                                                     DateTimeOffset nowUtc,
                                                     ILogger<ShareCleanupService>? logger = null) =>
        new(shareRepository,
            uploadedFileRepository,
            new InMemoryShareOperationClaimRepository(),
            blobStorage,
            new FrozenTimeProvider(nowUtc),
            logger ?? NullLogger<ShareCleanupService>.Instance);

    private static ShareRecord CreateShareRecord(Guid fileId, DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(),
            $"share-token-hash-{Guid.NewGuid():N}",
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            expiresAtUtc,
            null,
            ShareCleanupState.Pending,
            false,
            null,
            [new(fileId, "cipher.bin", null)]);

    private static UploadedFileRecord CreateUploadedFileRecord(Guid fileId) =>
        new(fileId,
            "blob-key",
            "cipher.bin",
            1,
            1,
            MediaTypeNames.Application.Octet,
            "1",
            "AES-256-GCM",
            1,
            1,
            "salt",
            "sha");

    private static async Task WaitForScheduledTimerAsync(ManualTimeProvider timeProvider)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (timeProvider.PendingTimerCount == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The hosted service did not schedule its next cleanup run.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class AlwaysMissingBlobStorage : IBlobStorage
    {
        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingBlobStorage : IBlobStorage
    {
        public TaskCompletionSource AllowDeleteToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
        {
            DeleteStarted.SetResult();
            await AllowDeleteToFinish.Task.WaitAsync(cancellationToken);
            return true;
        }

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<String> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public Boolean IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, String> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class CountingBlobStorage : IBlobStorage
    {
        public Int32 DeleteCalls { get; private set; }

        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.FromResult(false);
        }

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FailSecondDeleteBlobStorage : IBlobStorage
    {
        private Int32 _calls;

        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                throw new IOException("second delete failed");
            }

            return Task.FromResult(true);
        }

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FailSecondMetadataDeleteUploadedFileRepository(
        IEnumerable<UploadedFileRecord> records) : IUploadedFileMetadataRepository
    {
        private readonly Dictionary<Guid, UploadedFileRecord> _records = records.ToDictionary(record => record.FileId);
        private Int32 _deleteCalls;

        public IReadOnlyCollection<Guid> RemainingFileIds => _records.Keys;

        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult(_records.GetValueOrDefault(fileId));

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken)
        {
            if (!_records.ContainsKey(fileId))
            {
                return Task.FromResult(true);
            }

            if (Interlocked.Increment(ref _deleteCalls) == 2)
            {
                return Task.FromResult(false);
            }

            _records.Remove(fileId);
            return Task.FromResult(true);
        }

        public Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult(_records.ContainsKey(fileId));
    }

    private sealed class FlakyAccountingUploadedFileRepository(UploadedFileRecord record) : IUploadedFileMetadataRepository
    {
        public Int32 TransitionCalls { get; private set; }

        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadedFileRecord?>(fileId == record.FileId ? record : null);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord uploadedFile, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken)
        {
            TransitionCalls++;
            return Task.FromResult(TransitionCalls > 1);
        }
    }

    private sealed class FrozenTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class InMemoryShareRepository(ShareRecord share, Boolean failFirstDelete = false) : IShareMetadataRepository
    {
        private Int32 _deleteCalls;
        private ShareRecord? _share = share;

        public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) =>
            Task.FromResult(_share is { } current && current.ShareId == shareId ? current : null);

        public Task<ShareRecord?> GetByShareTokenHashAsync(
            String shareTokenHashBase64,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ShareRecord>>(_share is null ? [] : [_share]);

        public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryDeleteAsync(Guid shareId, CancellationToken cancellationToken)
        {
            if (_share?.ShareId != shareId)
            {
                return Task.FromResult(true);
            }

            if (failFirstDelete && Interlocked.Increment(ref _deleteCalls) == 1)
            {
                return Task.FromResult(false);
            }

            _share = null;
            return Task.FromResult(true);
        }

        public Task<Boolean> TryRecordCleanupAttemptAsync(
            Guid shareId,
            ShareCleanupState cleanupState,
            DateTimeOffset completedAtUtc,
            IReadOnlyCollection<String> failureCategories,
            CancellationToken cancellationToken)
        {
            if (_share is null || _share.ShareId != shareId)
            {
                return Task.FromResult(false);
            }

            _share = _share with
            {
                CleanupState = cleanupState,
                LastCleanupAttemptAtUtc = completedAtUtc,
                CleanupFailureCategories = failureCategories.ToArray()
            };
            return Task.FromResult(true);
        }

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryUploadedFileRepository(UploadedFileRecord record) : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadedFileRecord?>(record.FileId == fileId ? record : null);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult(record.FileId == fileId);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = utcNow;

        public Int32 PendingTimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _timers.Count;
                }
            }
        }

        public void Advance(TimeSpan delta)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _utcNow += delta;
                due = _timers.Where(timer => timer.DueAt <= _utcNow).ToArray();
                foreach (var timer in due)
                {
                    _timers.Remove(timer);
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, Object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ManualTimer(this, callback, state, _utcNow + dueTime);
                _timers.Add(timer);
                return timer;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider provider, TimerCallback callback, Object? state, DateTimeOffset dueAt) : ITimer
        {
            public DateTimeOffset DueAt { get; private set; } = dueAt;

            public void Fire() => callback(state);

            public ValueTask DisposeAsync()
            {
                provider.Remove(this);
                return ValueTask.CompletedTask;
            }

            public void Dispose() => provider.Remove(this);

            public Boolean Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAt = provider.GetUtcNow() + dueTime;
                return true;
            }
        }
    }

    private sealed class NeverAcquireCoordinator : IShareCleanupCoordinator
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class NoSweepCandidatesUploadedFileRepository : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UploadSweepCandidate>> GetSweepCandidatesAsync(
            DateTimeOffset completionCutoffUtc,
            Int32 limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UploadSweepCandidate>>([]);

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SequenceDeleteBlobStorage : IBlobStorage
    {
        private Int32 _calls;

        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) =>
            Task.FromResult(Interlocked.Increment(ref _calls) == 1);

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ShareCleanupFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "share-cleanup-tests",
                                                              Guid.NewGuid().ToString("N"));

        public ShareCleanupFixture()
        {
            Directory.CreateDirectory(_rootDirectory);
        }

        public ShadowDropOptions CreateOptions() =>
            new()
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(_rootDirectory, "metadata", "shadowdrop.db")
                },
                Storage = new()
                {
                    LocalRoot = Path.Combine(_rootDirectory, "storage")
                }
            };

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class SignalingShareRepository : IShareMetadataRepository
    {
        public SemaphoreSlim CleanupScanned { get; } = new(0);

        public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetByShareTokenHashAsync(
            String shareTokenHashBase64,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            CleanupScanned.Release();
            return Task.FromResult<IReadOnlyList<ShareRecord>>([]);
        }

        public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRecordCleanupAttemptAsync(Guid shareId, ShareCleanupState cleanupState, DateTimeOffset completedAtUtc,
                                                          IReadOnlyCollection<String> failureCategories,
                                                          CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingAcquireCoordinator : IShareCleanupCoordinator
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromException<IAsyncDisposable?>(new InvalidOperationException("lease acquisition failed"));
    }

    private sealed class ThrowingBlobStorage(Exception failure) : IBlobStorage
    {
        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => throw failure;

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingDisposeCoordinator : IShareCleanupCoordinator
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(new ThrowingLease());

        private sealed class ThrowingLease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.FromException(new InvalidOperationException("lease disposal failed"));
        }
    }

    private sealed class ThrowingReadUploadedFileRepository(Exception failure) : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => throw failure;

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
