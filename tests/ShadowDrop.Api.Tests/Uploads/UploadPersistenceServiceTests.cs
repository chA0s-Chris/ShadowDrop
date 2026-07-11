// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net.Mime;

public sealed class UploadPersistenceServiceTests
{
    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldInitializeAndPersist_WhenDatabaseFileDoesNotExist()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();
        var record = CreateRecord(Guid.NewGuid(), "metadata/test.blob");

        File.Exists(options.Metadata.LiteDbPath).Should().BeFalse();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var reservedRecord = await ReserveAndCompleteAsync(repository, record);
        var storedRecord = await repository.GetAsync(reservedRecord.FileId, CancellationToken.None);

        File.Exists(options.Metadata.LiteDbPath).Should().BeTrue();
        new FileInfo(options.Metadata.LiteDbPath).Length.Should().BeGreaterThan(0);
        storedRecord.Should().BeEquivalentTo(reservedRecord);
    }

    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldPruneExpiredReservations_WhenReservingNewFileId()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var expiredReservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        ExpireReservation(options.Metadata.LiteDbPath, expiredReservationId);

        await repository.ReserveFileIdAsync(CancellationToken.None);

        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(expiredReservationId)).Should().BeNull();
    }

    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldRejectClaimedReservationCompletion_WhenExpiredAfterClaim()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var reservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        var claimed = await repository.TryClaimReservationAsync(reservationId, CancellationToken.None);
        claimed.Should().BeTrue();

        ExpireReservation(options.Metadata.LiteDbPath, reservationId);

        var completed = await repository.TryCompleteReservationAsync(CreateRecord(reservationId, "metadata/claimed-expired.blob"), CancellationToken.None);

        completed.Should().BeFalse();
        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(reservationId)).Should().BeNull();
    }

    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldRejectExpiredClaimedReservationCompletion_AndPruneIt()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var reservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(reservationId, CancellationToken.None)).Should().BeTrue();
        ExpireReservation(options.Metadata.LiteDbPath, reservationId);

        var completed = await repository.TryCompleteReservationAsync(CreateRecord(reservationId, "metadata/expired.blob"), CancellationToken.None);

        completed.Should().BeFalse();
        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(reservationId)).Should().BeNull();
    }

    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldRejectExpiredReservationClaim_AndPruneIt()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var expiredReservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        ExpireReservation(options.Metadata.LiteDbPath, expiredReservationId);

        var claimed = await repository.TryClaimReservationAsync(expiredReservationId, CancellationToken.None);

        claimed.Should().BeFalse();
        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(expiredReservationId)).Should().BeNull();
    }

    [Test]
    public async Task LiteDbUploadedFileMetadataRepository_ShouldRejectExpiredReservationCompletion_AndPruneItWithoutNewReservation()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();

        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var expiredReservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        ExpireReservation(options.Metadata.LiteDbPath, expiredReservationId);

        var completed = await repository.TryCompleteReservationAsync(CreateRecord(expiredReservationId, "metadata/expired.blob"), CancellationToken.None);

        completed.Should().BeFalse();
        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(expiredReservationId)).Should().BeNull();
    }

    [Test]
    public async Task PersistAsync_ShouldDeleteBlob_WhenMetadataPersistenceFails()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        var repository = new ThrowingMetadataRepository();
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var request = CreateRequest(Guid.NewGuid());

        Func<Task> act = async () => await sut.PersistAsync(request, content, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.EnumerateFiles(options.Storage.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task PersistAsync_ShouldDeletePartialBlobAndReleaseClaim_WhenUploadIsCanceled()
    {
        await using var fixture = new UploadPersistenceFixture();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var request = await CreateReservedRequestAsync(repository);
        await using var content = new CancelingStream(CreateCiphertext());

        var act = async () => await sut.PersistAsync(request, content, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFiles(options.Storage.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        (await repository.TryClaimReservationAsync(request.FileId, CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task PersistAsync_ShouldLogCompletionWithSizesButNoSecretMaterial()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var collector = new FakeLogCollector();
        var sut = new UploadPersistenceService(blobStorage, repository, new FakeLogger<UploadPersistenceService>(collector));
        var request = await CreateReservedRequestAsync(repository);

        var result = await sut.PersistAsync(request, content, CancellationToken.None);

        var logRecords = collector.GetSnapshot();
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Information && logRecord.Message.Contains("Upload completed"));
        var completionRecord = logRecords.Single(logRecord => logRecord.Message.Contains("Upload completed"));
        var values = completionRecord.StructuredState!.Select(pair => pair.Value);
        values.Should().NotContain(value => value != null && value.Contains(request.KdfSaltBase64));
        values.Should().NotContain(value => value != null && value.Contains(request.PlaintextSha256!));
        values.Should().NotContain(value => value != null && value.Contains(request.OriginalFileName));
        completionRecord.StructuredState!.Should().Contain(pair => pair.Key == "FileId" && pair.Value == result.FileId.ToString());
    }

    [Test]
    public async Task PersistAsync_ShouldRejectCompletedFileIdWithoutDeletingExistingBlob()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var initialContent = CreateCiphertextStream();
        await using var duplicateContent = new MemoryStream(Enumerable.Repeat((Byte)99, 32).ToArray(), false);
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var request = await CreateReservedRequestAsync(repository);

        var initialResult = await sut.PersistAsync(request, initialContent, CancellationToken.None);
        var storedRecord = await repository.GetAsync(initialResult.FileId, CancellationToken.None);
        storedRecord.Should().NotBeNull();
        var blobPath = Path.Combine(options.Storage.LocalRoot, storedRecord!.BlobKey);
        var originalBlob = await File.ReadAllBytesAsync(blobPath);

        var act = async () => await sut.PersistAsync(request, duplicateContent, CancellationToken.None);

        await act.Should().ThrowAsync<UploadValidationException>()
                 .WithMessage("The file id is invalid or no longer available.");
        (await File.ReadAllBytesAsync(blobPath)).Should().Equal(originalBlob);
    }

    [Test]
    public async Task PersistAsync_ShouldRejectConcurrentUploadAfterAtomicClaim_WithoutCallingBlobStorageTwice()
    {
        var fileId = Guid.NewGuid();
        var repository = new AtomicClaimMetadataRepository(fileId);
        var blobStorage = new BlockingBlobStorage();
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var request = CreateRequest(fileId);
        await using var firstContent = CreateCiphertextStream();
        await using var secondContent = CreateCiphertextStream();

        var firstUploadTask = sut.PersistAsync(request, firstContent, CancellationToken.None);
        await blobStorage.SaveStarted.Task;

        var secondAttempt = async () => await sut.PersistAsync(request, secondContent, CancellationToken.None);

        await secondAttempt.Should().ThrowAsync<UploadValidationException>()
                           .WithMessage("The file id is invalid or no longer available.");
        blobStorage.SaveCallCount.Should().Be(1);

        blobStorage.AllowSaveToFinish.TrySetResult();
        var result = await firstUploadTask;

        result.FileId.Should().Be(fileId);
        repository.IsCompleted.Should().BeTrue();
    }

    [Test]
    public async Task PersistAsync_ShouldRejectExpiredReservation_AndNotPersistBlob()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var expiredReservationId = await repository.ReserveFileIdAsync(CancellationToken.None);
        ExpireReservation(options.Metadata.LiteDbPath, expiredReservationId);

        var act = async () => await sut.PersistAsync(CreateRequest(expiredReservationId), content, CancellationToken.None);

        await act.Should().ThrowAsync<UploadValidationException>()
                 .WithMessage("The file id is invalid or no longer available.");
        Directory.EnumerateFiles(options.Storage.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        (await repository.GetAsync(expiredReservationId, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task PersistAsync_ShouldWriteEncryptedBlobAndMetadata_WithRestrictivePermissions()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options, NullLogger<LocalBlobStorage>.Instance);
        using var repository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var sut = new UploadPersistenceService(blobStorage, repository, NullLogger<UploadPersistenceService>.Instance);
        var request = await CreateReservedRequestAsync(repository);

        var result = await sut.PersistAsync(request, content, CancellationToken.None);
        var storedRecord = await repository.GetAsync(result.FileId, CancellationToken.None);

        storedRecord.Should().NotBeNull();
        storedRecord!.FileId.Should().Be(request.FileId);
        storedRecord.OriginalFileName.Should().Be(request.OriginalFileName);
        storedRecord.BlobKey.Should().NotContain(request.OriginalFileName);
        storedRecord.KdfSaltBase64.Should().Be(request.KdfSaltBase64);
        storedRecord.PlaintextSha256.Should().Be(request.PlaintextSha256);

        var blobPath = Path.Combine(options.Storage.LocalRoot, storedRecord.BlobKey);
        File.Exists(blobPath).Should().BeTrue();
        var blobContent = await File.ReadAllBytesAsync(blobPath);
        blobContent.Should().Equal(CreateCiphertext());

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(blobPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(options.Metadata.LiteDbPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static Byte[] CreateCiphertext() => Enumerable.Range(0, 256).Select(value => (Byte)value).ToArray();

    private static Stream CreateCiphertextStream() => new MemoryStream(CreateCiphertext(), false);

    private static UploadedFileRecord CreateRecord(Guid fileId, String blobKey) =>
        new(fileId,
            blobKey,
            "document.enc",
            128,
            CreateCiphertext().LongLength,
            MediaTypeNames.Application.Octet,
            FormatConstants.EncryptionFormatVersion,
            FormatConstants.Aes256GcmAlgorithmId,
            64,
            2,
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            new('a', 64));

    private static UploadPersistenceRequest CreateRequest(Guid fileId) =>
        new(fileId,
            "document.enc",
            128,
            CreateCiphertext().LongLength,
            MediaTypeNames.Application.Octet,
            FormatConstants.EncryptionFormatVersion,
            FormatConstants.Aes256GcmAlgorithmId,
            64,
            2,
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            new('a', 64));

    private static async Task<UploadPersistenceRequest> CreateReservedRequestAsync(IUploadedFileMetadataRepository repository)
    {
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        return CreateRequest(fileId);
    }

    private static void ExpireReservation(String databasePath, Guid fileId)
    {
        using var database = new LiteDatabase(databasePath);
        var collection = database.GetCollection("uploaded_files");
        var document = collection.FindById(fileId);
        ((Object?)document).Should().NotBeNull();
        document!["ReservedAtUnixTimeMilliseconds"] = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        collection.Update(document);
    }

    private static async Task<UploadedFileRecord> ReserveAndCompleteAsync(IUploadedFileMetadataRepository repository, UploadedFileRecord record)
    {
        var reservedFileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(reservedFileId, CancellationToken.None)).Should().BeTrue();
        var reservedRecord = record with
        {
            FileId = reservedFileId
        };
        var completed = await repository.TryCompleteReservationAsync(reservedRecord, CancellationToken.None);
        completed.Should().BeTrue();
        return reservedRecord;
    }

    private sealed class AtomicClaimMetadataRepository(Guid reservedFileId) : IUploadedFileMetadataRepository
    {
        private readonly Lock _syncRoot = new();
        private Boolean _claimed;
        private Boolean _claimReleased;

        public Boolean IsCompleted { get; private set; }

        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult<UploadedFileRecord?>(null);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (fileId == reservedFileId && _claimed && !IsCompleted)
                {
                    _claimed = false;
                    _claimReleased = true;
                }
            }

            return Task.CompletedTask;
        }

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => Task.FromResult(reservedFileId);

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (fileId != reservedFileId || _claimed || IsCompleted || _claimReleased)
                {
                    return Task.FromResult(false);
                }

                _claimed = true;
                return Task.FromResult(true);
            }
        }

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (record.FileId != reservedFileId || !_claimed || _claimReleased)
                {
                    return Task.FromResult(false);
                }

                _claimed = false;
                IsCompleted = true;
                return Task.FromResult(true);
            }
        }
    }

    private sealed class BlockingBlobStorage : IBlobStorage
    {
        public TaskCompletionSource AllowSaveToFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Int32 SaveCallCount { get; private set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            SaveStarted.TrySetResult();
            await AllowSaveToFinish.Task.WaitAsync(cancellationToken);
            return new($"blob/{fileId:N}.blob", encryptedContent.Length);
        }
    }

    private sealed class CancelingStream(Byte[] content) : MemoryStream(content, false)
    {
        private Boolean _hasRead;

        public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                throw new OperationCanceledException("Simulated interrupted request body.");
            }

            _hasRead = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 32)], cancellationToken);
        }
    }

    private sealed class ThrowingMetadataRepository : IUploadedFileMetadataRepository
    {
        public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult<UploadedFileRecord?>(null);

        public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated metadata write failure.");
    }

    private sealed class UploadPersistenceFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "upload-tests",
                                                              Guid.NewGuid().ToString("N"));

        public UploadPersistenceFixture()
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
