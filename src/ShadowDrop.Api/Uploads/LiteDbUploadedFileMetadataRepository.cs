// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LiteDbUploadedFileMetadataRepository : IUploadedFileMetadataRepository, IDisposable
{
    private static readonly TimeSpan ReservationRetention = TimeSpan.FromDays(1);
    private readonly ILiteCollection<UploadedFileDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;

    public LiteDbUploadedFileMetadataRepository(ShadowDropOptions options)
    {
        _databasePath = options.Metadata.LiteDbPath;
        var databaseDirectory = Path.GetDirectoryName(_databasePath)
                                ?? throw new InvalidOperationException("The metadata database path must include a directory.");
        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(databaseDirectory);

        _database = new(new ConnectionString
        {
            Filename = _databasePath,
            Connection = ConnectionType.Shared
        });

        try
        {
            _collection = _database.GetCollection<UploadedFileDocument>("uploaded_files");
            _collection.EnsureIndex(document => document.FileId, true);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    private static UploadedFileRecord Map(UploadedFileDocument document) =>
        new(document.FileId,
            document.BlobKey,
            document.OriginalFileName,
            document.PlaintextLength,
            document.EncryptedLength,
            document.ContentType,
            document.EncryptionFormatVersion,
            document.AlgorithmId,
            document.ChunkSize,
            document.ChunkCount,
            document.KdfSaltBase64,
            document.PlaintextSha256);

    private void DeleteExpiredReservations()
    {
        var deletedCount = _collection.DeleteMany(document =>
                                                      document.IsReserved
                                                      && document.ReservedAtUnixTimeMilliseconds.HasValue
                                                      && document.ReservedAtUnixTimeMilliseconds.Value <=
                                                      DateTimeOffset.UtcNow.Subtract(ReservationRetention).ToUnixTimeMilliseconds());

        if (deletedCount > 0)
        {
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
    }

    public void Dispose() => _database.Dispose();

    public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(fileId);
        return Task.FromResult(document is null || document.IsReserved ? null : Map(document));
    }

    public Task<Boolean> HasActiveReservationAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(fileId);
        return Task.FromResult(document is not null && document.IsReserved);
    }

    public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteExpiredReservations();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = Guid.NewGuid();
            if (_collection.Exists(document => document.FileId == fileId))
            {
                continue;
            }

            _collection.Insert(new UploadedFileDocument
            {
                FileId = fileId,
                IsReserved = true,
                ReservedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(fileId);
        }
    }

    public Task SaveAsync(UploadedFileRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        _collection.Upsert(new UploadedFileDocument
        {
            FileId = record.FileId,
            BlobKey = record.BlobKey,
            OriginalFileName = record.OriginalFileName,
            PlaintextLength = record.PlaintextLength,
            EncryptedLength = record.EncryptedLength,
            ContentType = record.ContentType,
            EncryptionFormatVersion = record.EncryptionFormatVersion,
            AlgorithmId = record.AlgorithmId,
            IsReserved = false,
            ChunkSize = record.ChunkSize,
            ChunkCount = record.ChunkCount,
            KdfSaltBase64 = record.KdfSaltBase64,
            PlaintextSha256 = record.PlaintextSha256
        });
        FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        return Task.CompletedTask;
    }

    public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(record.FileId);
        if (document is null || !document.IsReserved)
        {
            return Task.FromResult(false);
        }

        _collection.Update(new UploadedFileDocument
        {
            FileId = record.FileId,
            BlobKey = record.BlobKey,
            OriginalFileName = record.OriginalFileName,
            PlaintextLength = record.PlaintextLength,
            EncryptedLength = record.EncryptedLength,
            ContentType = record.ContentType,
            EncryptionFormatVersion = record.EncryptionFormatVersion,
            AlgorithmId = record.AlgorithmId,
            IsReserved = false,
            ReservedAtUnixTimeMilliseconds = null,
            ChunkSize = record.ChunkSize,
            ChunkCount = record.ChunkCount,
            KdfSaltBase64 = record.KdfSaltBase64,
            PlaintextSha256 = record.PlaintextSha256
        });
        FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        return Task.FromResult(true);
    }

    private sealed class UploadedFileDocument
    {
        public String AlgorithmId { get; set; } = String.Empty;

        public String BlobKey { get; set; } = String.Empty;

        public Int64 ChunkCount { get; set; }

        public Int32 ChunkSize { get; set; }

        public String? ContentType { get; set; }

        public Int64 EncryptedLength { get; set; }

        public String EncryptionFormatVersion { get; set; } = String.Empty;

        [BsonId]
        public Guid FileId { get; set; }

        public Boolean IsReserved { get; set; }

        public String KdfSaltBase64 { get; set; } = String.Empty;

        public String OriginalFileName { get; set; } = String.Empty;

        public Int64 PlaintextLength { get; set; }

        public String? PlaintextSha256 { get; set; }

        public Int64? ReservedAtUnixTimeMilliseconds { get; set; }
    }
}
