// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using LiteDB;
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

        using var repository = new LiteDbUploadedFileMetadataRepository(options);
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

        using var repository = new LiteDbUploadedFileMetadataRepository(options);
        var expiredReservationId = await repository.ReserveFileIdAsync(CancellationToken.None);

        using (var database = new LiteDatabase(options.Metadata.LiteDbPath))
        {
            var collection = database.GetCollection("uploaded_files");
            var document = collection.FindById(expiredReservationId);
            ((Object?)document).Should().NotBeNull();
            document!["ReservedAtUnixTimeMilliseconds"] = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
            collection.Update(document);
        }

        await repository.ReserveFileIdAsync(CancellationToken.None);

        using var verificationDatabase = new LiteDatabase(options.Metadata.LiteDbPath);
        ((Object?)verificationDatabase.GetCollection("uploaded_files").FindById(expiredReservationId)).Should().BeNull();
    }

    [Test]
    public async Task PersistAsync_ShouldDeleteBlob_WhenMetadataPersistenceFails()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options);
        var repository = new ThrowingMetadataRepository();
        var sut = new UploadPersistenceService(blobStorage, repository);
        var request = CreateRequest(Guid.NewGuid());

        Func<Task> act = async () => await sut.PersistAsync(request, content, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.EnumerateFiles(options.Storage.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Test]
    public async Task PersistAsync_ShouldWriteEncryptedBlobAndMetadata_WithRestrictivePermissions()
    {
        await using var fixture = new UploadPersistenceFixture();
        await using var content = CreateCiphertextStream();
        var options = fixture.CreateOptions();
        var blobStorage = new LocalBlobStorage(options);
        using var repository = new LiteDbUploadedFileMetadataRepository(options);
        var sut = new UploadPersistenceService(blobStorage, repository);
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

    private static async Task<UploadedFileRecord> ReserveAndCompleteAsync(IUploadedFileMetadataRepository repository, UploadedFileRecord record)
    {
        var reservedFileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        var reservedRecord = record with
        {
            FileId = reservedFileId
        };
        var completed = await repository.TryCompleteReservationAsync(reservedRecord, CancellationToken.None);
        completed.Should().BeTrue();
        return reservedRecord;
    }

    private sealed class ThrowingMetadataRepository : IUploadedFileMetadataRepository
    {
        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult<UploadedFileRecord?>(null);

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());

        public Task SaveAsync(UploadedFileRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

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
