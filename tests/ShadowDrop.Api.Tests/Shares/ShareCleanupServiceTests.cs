// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using System.Net.Mime;

public sealed class ShareCleanupServiceTests
{
    [Test]
    public async Task RunAsync_ShouldCleanupRevokedShare_EvenWhenItHasNotExpired()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-10T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        (await shareRepository.TryRevokeAsync(share.ShareId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"), CancellationToken.None))
            .Should().BeTrue();
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
    }

    [Test]
    public async Task RunAsync_ShouldDeleteBlobAndCompleteExpiredShare_WithoutDeletingUploadMetadata()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        File.Exists(Path.Combine(options.Storage.LocalRoot, uploadedFile.BlobKey)).Should().BeFalse();
        (await uploadedFileRepository.GetAsync(uploadedFile.FileId, CancellationToken.None)).Should().NotBeNull();
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
    }

    [Test]
    public async Task RunAsync_ShouldMarkShareFailed_WhenUploadMetadataIsMissing_AndRetryFailedShare()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options);
        var share = CreateShareRecord(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var firstResult = await sut.RunAsync(CancellationToken.None);
        var secondResult = await sut.RunAsync(CancellationToken.None);

        firstResult.Should().Be(new ShareCleanupResult(1, 0, 0, 0, 1));
        secondResult.Should().Be(new ShareCleanupResult(1, 0, 0, 0, 1));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
    }

    [Test]
    public async Task RunAsync_ShouldTreatMissingBlobAsCompletedAndRemainIdempotent()
    {
        await using var fixture = new ShareCleanupFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var blobStorage = new LocalBlobStorage(options);
        var uploadedFile = await CompleteUploadAsync(uploadedFileRepository, blobStorage);
        File.Delete(Path.Combine(options.Storage.LocalRoot, uploadedFile.BlobKey));
        var share = CreateShareRecord(uploadedFile.FileId, DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var sut = CreateService(shareRepository, uploadedFileRepository, blobStorage, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var firstResult = await sut.RunAsync(CancellationToken.None);
        var secondResult = await sut.RunAsync(CancellationToken.None);

        firstResult.Should().Be(new ShareCleanupResult(1, 1, 0, 1, 0));
        secondResult.Should().Be(new ShareCleanupResult(0, 0, 0, 0, 0));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
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
        using var runner = new ShareCleanupRunner(cleanupService, NullLogger<ShareCleanupRunner>.Instance);

        var firstRun = runner.RunIfIdleAsync(CancellationToken.None);
        await blobStorage.DeleteStarted.Task;
        var secondRun = await runner.RunIfIdleAsync(CancellationToken.None);
        blobStorage.AllowDeleteToFinish.SetResult();
        var firstResult = await firstRun;

        secondRun.Skipped.Should().BeTrue();
        firstResult.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
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

    private static ShareCleanupService CreateService(IShareMetadataRepository shareRepository,
                                                     IUploadedFileMetadataRepository uploadedFileRepository,
                                                     IBlobStorage blobStorage,
                                                     DateTimeOffset nowUtc) =>
        new(shareRepository,
            uploadedFileRepository,
            blobStorage,
            new FrozenTimeProvider(nowUtc),
            NullLogger<ShareCleanupService>.Instance);

    private static ShareRecord CreateShareRecord(Guid fileId, DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(),
            $"share-token-hash-{Guid.NewGuid():N}",
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            expiresAtUtc,
            null,
            ShareCleanupState.Pending,
            DirectHttpEnabled: false,
            DownloadBearerToken: null,
            [new(fileId, "cipher.bin", null)]);

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

    private sealed class FrozenTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class InMemoryShareRepository(ShareRecord share) : IShareMetadataRepository
    {
        private ShareRecord _share = share;

        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) =>
            Task.FromResult<ShareRecord?>(_share.ShareId == shareId ? _share : null);

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ShareRecord>>([_share]);

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryUpdateCleanupStateAsync(Guid shareId, ShareCleanupState cleanupState, CancellationToken cancellationToken)
        {
            if (_share.ShareId != shareId)
            {
                return Task.FromResult(false);
            }

            _share = _share with
            {
                CleanupState = cleanupState
            };
            return Task.FromResult(true);
        }
    }

    private sealed class InMemoryUploadedFileRepository(UploadedFileRecord record) : IUploadedFileMetadataRepository
    {
        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadedFileRecord?>(record.FileId == fileId ? record : null);

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
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
}
