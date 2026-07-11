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
                                                     new BlockingBlobStorage(),
                                                     timeProvider,
                                                     NullLogger<ShareCleanupService>.Instance);
        using var coordinator = new InProcessShareCleanupCoordinator();
        var runner = new ShareCleanupRunner(cleanupService, coordinator, NullLogger<ShareCleanupRunner>.Instance);
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
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
    }

    [Test]
    public async Task RunAsync_ShouldDeleteBlobAndCompleteExpiredShare_WithoutDeletingUploadMetadata()
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
        (await uploadedFileRepository.GetAsync(uploadedFile.FileId, CancellationToken.None)).Should().NotBeNull();
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
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

        // A share referencing a file that has no upload metadata forces the cleanup run to record a failure.
        await shareRepository.CreateAsync(CreateShareRecord(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-01T00:00:00Z")), CancellationToken.None);
        var collector = new FakeLogCollector();
        var sut = CreateService(shareRepository,
                                uploadedFileRepository,
                                blobStorage,
                                DateTimeOffset.Parse("2026-06-02T00:00:00Z"),
                                new FakeLogger<ShareCleanupService>(collector));

        var result = await sut.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(1);
        var completionRecord = collector.GetSnapshot().Single(logRecord => logRecord.Message.Contains("Share cleanup completed with failures"));
        completionRecord.Level.Should().Be(LogLevel.Warning);
        completionRecord.StructuredState!.Should().Contain(pair => pair.Key == "Failures" && pair.Value == "1");
    }

    [Test]
    public async Task RunAsync_ShouldMarkShareFailed_WhenUploadMetadataIsMissing_AndRetryFailedShare()
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

        firstResult.Should().Be(new ShareCleanupResult(1, 0, 0, 0, 1));
        secondResult.Should().Be(new ShareCleanupResult(1, 0, 0, 0, 1));
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
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
                                    DirectHttpEnabled: false,
                                    new DownloadBearerTokenRecord(secretMaterial, DateTimeOffset.Parse("2026-06-01T00:00:00Z")),
                                    [new(uploadedFile.FileId, "cipher.bin", null)]);
        await shareRepository.CreateAsync(share, CancellationToken.None);
        var logger = new CapturingLogger<ShareCleanupService>();
        var sut = new ShareCleanupService(shareRepository,
                                          uploadedFileRepository,
                                          blobStorage,
                                          new FrozenTimeProvider(DateTimeOffset.Parse("2026-06-02T00:00:00Z")),
                                          logger);

        var result = await sut.RunAsync(CancellationToken.None);

        result.Should().Be(new ShareCleanupResult(1, 1, 1, 0, 0));
        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(message => message.Contains(secretMaterial));
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
        (await shareRepository.GetAsync(share.ShareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
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
        var sut = new ShareCleanupRunner(cleanupService, coordinator, new FakeLogger<ShareCleanupRunner>(collector));

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
        var runner = new ShareCleanupRunner(cleanupService, coordinator, new FakeLogger<ShareCleanupRunner>(collector));

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
                                                     DateTimeOffset nowUtc,
                                                     ILogger<ShareCleanupService>? logger = null) =>
        new(shareRepository,
            uploadedFileRepository,
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
            DirectHttpEnabled: false,
            DownloadBearerToken: null,
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

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadedFileRecord?>(record.FileId == fileId ? record : null);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            CleanupScanned.Release();
            return Task.FromResult<IReadOnlyList<ShareRecord>>([]);
        }

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryUpdateCleanupStateAsync(Guid shareId, ShareCleanupState cleanupState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
