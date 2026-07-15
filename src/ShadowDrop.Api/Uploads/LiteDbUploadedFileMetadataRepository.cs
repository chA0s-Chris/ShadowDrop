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
    private readonly ILogger<LiteDbUploadedFileMetadataRepository> _logger;
    private readonly Lock _syncRoot = new();

    public LiteDbUploadedFileMetadataRepository(ShadowDropOptions options, ILogger<LiteDbUploadedFileMetadataRepository> logger)
    {
        _logger = logger;
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

    private static Int64 GetReservationCutoffUnixTimeMilliseconds(DateTimeOffset now) =>
        now.Subtract(ReservationRetention).ToUnixTimeMilliseconds();

    private static Boolean IsActiveReservation(UploadedFileDocument? document, DateTimeOffset now) =>
        document is { IsReserved: true, IsClaimed: false, ReservedAtUnixTimeMilliseconds: not null }
        && document.ReservedAtUnixTimeMilliseconds.Value > GetReservationCutoffUnixTimeMilliseconds(now);

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
            document.PlaintextSha256,
            document.OwnerCredentialId);

    private Boolean DeleteExpiredReservation(UploadedFileDocument? document, DateTimeOffset now)
    {
        if (document is null
            || !document.IsReserved
            || !document.ReservedAtUnixTimeMilliseconds.HasValue
            || document.ReservedAtUnixTimeMilliseconds.Value > GetReservationCutoffUnixTimeMilliseconds(now))
        {
            return false;
        }

        var deleted = _collection.Delete(document.FileId);
        if (deleted)
        {
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            _logger.LogInformation("Upload reservation expired. FileId: {FileId}", document.FileId);
        }

        return deleted;
    }

    private void DeleteExpiredReservations()
    {
        var deletedCount = _collection.DeleteMany(document =>
                                                      document.IsReserved
                                                      && document.ReservedAtUnixTimeMilliseconds.HasValue
                                                      && document.ReservedAtUnixTimeMilliseconds.Value <=
                                                      GetReservationCutoffUnixTimeMilliseconds(DateTimeOffset.UtcNow));

        if (deletedCount > 0)
        {
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            _logger.LogInformation("Expired upload reservations pruned. Count: {Count}", deletedCount);
        }
    }

    private Task<Guid> ReserveFileIdAsync(Guid? ownerCredentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
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
                    IsClaimed = false,
                    OwnerCredentialId = ownerCredentialId,
                    ReservedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
                _logger.LogInformation("Upload reservation created. FileId: {FileId}", fileId);
                return Task.FromResult(fileId);
            }
        }
    }

    private Task<Boolean> TryClaimReservationAsync(Guid fileId, Guid? ownerCredentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var document = _collection.FindById(fileId);
            if (!IsActiveReservation(document, now) || document!.OwnerCredentialId != ownerCredentialId)
            {
                if (!DeleteExpiredReservation(document, now))
                {
                    _logger.LogDebug(
                        "Upload reservation claim rejected because the reservation was missing or already claimed. FileId: {FileId}",
                        fileId);
                }

                return Task.FromResult(false);
            }

            document.IsClaimed = true;
            _collection.Update(document);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            _logger.LogInformation("Upload reservation claimed. FileId: {FileId}", fileId);
            return Task.FromResult(true);
        }
    }

    public void Dispose() => _database.Dispose();

    public Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var cutoff = GetReservationCutoffUnixTimeMilliseconds(now);
        var count = _collection.Count(document => document.IsReserved
                                                  && !document.IsClaimed
                                                  && document.ReservedAtUnixTimeMilliseconds != null
                                                  && document.ReservedAtUnixTimeMilliseconds.Value > cutoff);
        return Task.FromResult(count);
    }

    public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(fileId);
        return Task.FromResult(document is null || document.IsReserved ? null : Map(document));
    }

    public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completedFileCount = 0;
        var totalEncryptedBytes = 0L;
        foreach (var document in _collection.Find(document => !document.IsReserved))
        {
            completedFileCount++;
            totalEncryptedBytes += document.EncryptedLength;
        }

        return Task.FromResult(new UploadedFileStorageStats(completedFileCount, totalEncryptedBytes));
    }

    public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var document = _collection.FindById(fileId);
            if (document is not { IsReserved: true, IsClaimed: true })
            {
                return Task.CompletedTask;
            }

            if (document.ReservedAtUnixTimeMilliseconds.HasValue
                && document.ReservedAtUnixTimeMilliseconds.Value <= GetReservationCutoffUnixTimeMilliseconds(now))
            {
                DeleteExpiredReservation(document, now);
                return Task.CompletedTask;
            }

            document.IsClaimed = false;
            _collection.Update(document);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            _logger.LogInformation("Upload reservation released. FileId: {FileId}", fileId);
            return Task.CompletedTask;
        }
    }

    public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) =>
        ReserveFileIdAsync(null, cancellationToken);

    public Task<Guid> ReserveFileIdAsync(Guid ownerCredentialId, CancellationToken cancellationToken) =>
        ReserveFileIdAsync((Guid?)ownerCredentialId, cancellationToken);


    public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) =>
        TryClaimReservationAsync(fileId, null, cancellationToken);

    public Task<Boolean> TryClaimReservationAsync(Guid fileId, Guid ownerCredentialId, CancellationToken cancellationToken) =>
        TryClaimReservationAsync(fileId, (Guid?)ownerCredentialId, cancellationToken);

    public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var document = _collection.FindById(record.FileId);
            if (document is not { IsReserved: true })
            {
                _logger.LogWarning(
                    "Upload reservation completion rejected because the reservation was missing. FileId: {FileId}",
                    record.FileId);
                return Task.FromResult(false);
            }

            if (!document.IsClaimed)
            {
                if (!DeleteExpiredReservation(document, now))
                {
                    _logger.LogWarning(
                        "Upload reservation completion rejected because the reservation was not claimed. FileId: {FileId}",
                        record.FileId);
                }

                return Task.FromResult(false);
            }

            if (document.ReservedAtUnixTimeMilliseconds.HasValue
                && document.ReservedAtUnixTimeMilliseconds.Value <= GetReservationCutoffUnixTimeMilliseconds(now))
            {
                DeleteExpiredReservation(document, now);
                return Task.FromResult(false);
            }

            if (document.OwnerCredentialId != record.OwnerCredentialId)
            {
                _logger.LogWarning(
                    "Upload reservation completion rejected because the owner did not match. FileId: {FileId}",
                    record.FileId);
                return Task.FromResult(false);
            }

            _collection.Update(new UploadedFileDocument
            {
                FileId = record.FileId,
                BlobKey = record.BlobKey,
                OriginalFileName = record.OriginalFileName,
                OwnerCredentialId = document.OwnerCredentialId,
                PlaintextLength = record.PlaintextLength,
                EncryptedLength = record.EncryptedLength,
                ContentType = record.ContentType,
                EncryptionFormatVersion = record.EncryptionFormatVersion,
                AlgorithmId = record.AlgorithmId,
                IsReserved = false,
                IsClaimed = false,
                ReservedAtUnixTimeMilliseconds = null,
                ChunkSize = record.ChunkSize,
                ChunkCount = record.ChunkCount,
                KdfSaltBase64 = record.KdfSaltBase64,
                PlaintextSha256 = record.PlaintextSha256
            });
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            // UploadPersistenceService logs the operator-facing "Upload completed" event with a superset of these fields.
            _logger.LogDebug("Upload reservation completed. FileId: {FileId}; BlobKey: {BlobKey}", record.FileId, record.BlobKey);
            return Task.FromResult(true);
        }
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

        public Boolean IsClaimed { get; set; }

        public Boolean IsReserved { get; set; }

        public String KdfSaltBase64 { get; set; } = String.Empty;

        public String OriginalFileName { get; set; } = String.Empty;

        public Guid? OwnerCredentialId { get; set; }

        public Int64 PlaintextLength { get; set; }

        public String? PlaintextSha256 { get; set; }

        public Int64? ReservedAtUnixTimeMilliseconds { get; set; }
    }
}
