// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using System.Net.Mime;

public sealed class CreateShareServiceTests
{
    [Test]
    public async Task CreateAsync_ShouldAcquireWholeOperationClaimBeforeReadingUploadMetadata()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = Guid.NewGuid();
        (await claimRepository.TryAcquireAsync(Guid.NewGuid(),
                                               ShareOperationClaimKind.CleanupShare,
                                               Guid.NewGuid(),
                                               [fileId],
                                               CancellationToken.None)).Should().NotBeNull();
        var sut = new CreateShareService(new ThrowingReadUploadedFileRepository(),
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
                                             [new(fileId)],
                                             GenerateDownloadBearerToken: false);

        var act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldLogCreationWithoutTokenValues()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var collector = new FakeLogCollector();
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         new FakeLogger<CreateShareService>(collector));
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId, "Display.bin")],
                                             GenerateDownloadBearerToken: true,
                                             DownloadBearerTokenExpiresAtUtc: DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        var result = await sut.CreateAsync(request, CancellationToken.None);

        var logRecords = collector.GetSnapshot();
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Information && logRecord.Message.Contains("Share created"));
        var creationRecord = logRecords.Single(logRecord => logRecord.Message.Contains("Share created"));
        var values = creationRecord.StructuredState!.Select(pair => pair.Value).ToList();
        values.Should().NotContain(value => value != null && value.Contains(result.ShareToken));
        values.Should().NotContain(value => value != null && value.Contains(result.DownloadBearerToken!));
        creationRecord.StructuredState!.Should().Contain(pair => pair.Key == "ShareId" && pair.Value == result.ShareId.ToString());
    }

    [Test]
    public async Task CreateAsync_ShouldPersistHashedTokensAndMetadata()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId, "Display.bin")],
                                             GenerateDownloadBearerToken: true,
                                             DownloadBearerTokenExpiresAtUtc: DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        var result = await sut.CreateAsync(request, CancellationToken.None);
        var storedShare = await shareRepository.GetAsync(result.ShareId, CancellationToken.None);

        storedShare.Should().NotBeNull();
        storedShare.ShareTokenHashBase64.Should().NotBe(result.ShareToken);
        storedShare.DownloadBearerToken.Should().NotBeNull();
        storedShare.DownloadBearerToken!.TokenHashBase64.Should().NotBe(result.DownloadBearerToken);
        storedShare.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        storedShare.ExpiresAtUtc.Should().Be(request.ExpiresAtUtc);
        storedShare.RevokedAtUtc.Should().BeNull();
        storedShare.CleanupState.Should().Be(ShareCleanupState.Pending);
        storedShare.DirectHttpEnabled.Should().BeFalse();
        storedShare.Files.Should().ContainSingle();
        storedShare.Files[0].FileId.Should().Be(fileId);
        storedShare.Files[0].OriginalFileName.Should().Be("cipher.bin");
        storedShare.Files[0].DisplayName.Should().Be("Display.bin");
        result.ShareToken.Should().NotBeNullOrWhiteSpace();
        result.ShareToken.Length.Should().BeGreaterThanOrEqualTo(43);
        result.DownloadBearerToken.Should().NotBeNullOrWhiteSpace();
        result.DownloadBearerToken!.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    // An indeterminate insert has two possible outcomes, and recovery may assume neither: the write either
    // reached the store before the failure surfaced or it did not. Both must converge on one share carrying
    // the claim's original identifier.
    [TestCase(true)]
    [TestCase(false)]
    public async Task CreateAsync_ShouldRecoverIndeterminateInsertWithSameShareIdentifier(Boolean insertLanded)
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploads = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shares = new LiteDbShareMetadataRepository(options);
        using var claims = new LiteDbShareOperationClaimRepository(options);
        var firstFileId = await ReserveAndCompleteAsync(uploads, CreateUploadedFileRecord(Guid.NewGuid(), "first.bin"));
        var interrupted = new IndeterminateInsertShareRepository(shares, insertLanded);
        var firstService = new CreateShareService(uploads,
                                                  interrupted,
                                                  claims,
                                                  TimeProvider.System,
                                                  NullLogger<CreateShareService>.Instance);

        var firstAttempt = async () => await firstService.CreateAsync(
            new(DateTimeOffset.UtcNow.AddDays(1), [new(firstFileId)], GenerateDownloadBearerToken: false),
            CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<TimeoutException>();
        var unfinished = (await claims.GetUnfinishedShareCreationsAsync([firstFileId], CancellationToken.None)).Should().ContainSingle().Subject;
        unfinished.Lifecycle.Should().Be(ShareOperationClaimLifecycle.Committing);
        (await shares.GetAsync(unfinished.ShareId, CancellationToken.None) is not null).Should().Be(
            insertLanded, "the claim must survive the failure whether or not the insert reached the store");

        var recoveryService = new CreateShareService(uploads,
                                                     shares,
                                                     claims,
                                                     TimeProvider.System,
                                                     NullLogger<CreateShareService>.Instance);
        var recoveryAttempt = async () => await recoveryService.CreateAsync(
            new(DateTimeOffset.UtcNow.AddDays(1), [new(firstFileId)], GenerateDownloadBearerToken: false),
            CancellationToken.None);

        await recoveryAttempt.Should().ThrowAsync<CreateShareValidationException>();
        (await shares.GetAsync(unfinished.ShareId, CancellationToken.None)).Should().NotBeNull();
        (await claims.GetUnfinishedShareCreationsAsync([firstFileId], CancellationToken.None)).Should().BeEmpty();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectDuplicateFileIds()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId), new(fileId)],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_ShouldRejectFileIdsAlreadyReferencedByExistingShare(Boolean revokeExistingShare)
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var firstRequest = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                                  [new(fileId)],
                                                  GenerateDownloadBearerToken: false);
        var firstShare = await sut.CreateAsync(firstRequest, CancellationToken.None);
        if (revokeExistingShare)
        {
            (await shareRepository.TryRevokeAsync(firstShare.ShareId,
                                                  DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                                                  CancellationToken.None)).Should().BeTrue();
        }

        var secondRequest = new CreateShareRequest(DateTimeOffset.Parse("2026-06-02T00:00:00Z"),
                                                   [new(fileId)],
                                                   GenerateDownloadBearerToken: false);
        Func<Task> act = async () => await sut.CreateAsync(secondRequest, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
        (await claimRepository.GetUnfinishedShareCreationsAsync([fileId], CancellationToken.None)).Should().BeEmpty();
    }

    [TestCase(true, true, true)]
    [TestCase(false, null, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    public async Task CreateAsync_ShouldRejectInvalidModeOrTokenCombinations(Boolean directHttpEnabled,
                                                                             Boolean? generateDownloadBearerToken,
                                                                             Boolean includeDownloadTokenExpiration)
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId)],
                                             directHttpEnabled,
                                             generateDownloadBearerToken,
                                             includeDownloadTokenExpiration ? DateTimeOffset.Parse("2026-05-30T00:00:00Z") : null);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectMissingFiles()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var sut = new CreateShareService(uploadedFileRepository,
                                         shareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(Guid.NewGuid())],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRollback_WhenMetadataCommitFails()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var failingShareRepository = new LiteDbShareMetadataRepository(options, () => throw new InvalidOperationException("Simulated transaction failure."));
        using var claimRepository = new LiteDbShareOperationClaimRepository(options);
        var sut = new CreateShareService(uploadedFileRepository,
                                         failingShareRepository,
                                         claimRepository,
                                         TimeProvider.System,
                                         NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId)],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        failingShareRepository.Dispose();
        using var database = new LiteDatabase(options.Metadata.LiteDbPath);
        database.GetCollection("shares").Count().Should().Be(0);
    }

    private static UploadedFileRecord CreateUploadedFileRecord(Guid fileId, String originalFileName) =>
        new(fileId,
            $"metadata/{fileId:N}.blob",
            originalFileName,
            128,
            256,
            MediaTypeNames.Application.Octet,
            "1",
            "AES-256-GCM",
            64,
            2,
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            new('a', 64));


    private static async Task<Guid> ReserveAndCompleteAsync(IUploadedFileMetadataRepository repository, UploadedFileRecord record)
    {
        var reservedFileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(reservedFileId, CancellationToken.None)).Should().BeTrue();
        var completed = await repository.TryCompleteReservationAsync(record with
        {
            FileId = reservedFileId
        }, CancellationToken.None);
        completed.Should().BeTrue();
        return reservedFileId;
    }

    private sealed class IndeterminateInsertShareRepository(IShareMetadataRepository inner, Boolean insertLanded)
        : IShareMetadataRepository
    {
        private Int32 _createCalls;

        public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken) =>
            inner.CountMatchingAsync(query, cancellationToken);

        public async Task CreateAsync(ShareRecord record, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _createCalls) != 1)
            {
                await inner.CreateAsync(record, cancellationToken);
                return;
            }

            if (insertLanded)
            {
                await inner.CreateAsync(record, cancellationToken);
            }

            throw new TimeoutException("insert outcome was indeterminate");
        }

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) =>
            inner.GetAsync(shareId, cancellationToken);

        public Task<ShareRecord?> GetByShareTokenHashAsync(
            String shareTokenHashBase64,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            inner.GetByShareTokenHashAsync(shareTokenHashBase64, nowUtc, cancellationToken);

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            inner.GetCleanupCandidatesAsync(nowUtc, cancellationToken);

        public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken) =>
            inner.GetListPageAsync(query, cancellationToken);

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            inner.GetStatusCountsAsync(nowUtc, cancellationToken);

        public Task<Boolean> TryRecordCleanupAttemptAsync(
            Guid shareId,
            ShareCleanupState cleanupState,
            DateTimeOffset completedAtUtc,
            IReadOnlyCollection<String> failureCategories,
            CancellationToken cancellationToken) =>
            inner.TryRecordCleanupAttemptAsync(shareId, cleanupState, completedAtUtc, failureCategories, cancellationToken);

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            inner.TryRevokeAsync(shareId, revokedAtUtc, cancellationToken);
    }

    private sealed class SharePersistenceFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "share-tests",
                                                              Guid.NewGuid().ToString("N"));

        public SharePersistenceFixture()
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

    private sealed class ThrowingReadUploadedFileRepository : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            throw new AssertionException("Upload metadata must not be read before operation-claim acquisition succeeds.");

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
