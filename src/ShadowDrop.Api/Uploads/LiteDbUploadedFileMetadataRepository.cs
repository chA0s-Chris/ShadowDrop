// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

public sealed class LiteDbUploadedFileMetadataRepository : IUploadedFileMetadataRepository, IDisposable
{
    private static readonly TimeSpan ReservationRetention = TimeSpan.FromDays(1);
    private readonly ILiteCollection<UploadedFileDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly ILogger<LiteDbUploadedFileMetadataRepository> _logger;
    private readonly Action? _storageStatsIterationTestHook;
    private readonly Lock _syncRoot = new();

    public LiteDbUploadedFileMetadataRepository(ShadowDropOptions options, ILogger<LiteDbUploadedFileMetadataRepository> logger)
        : this(options, logger, null) { }

    internal LiteDbUploadedFileMetadataRepository(
        ShadowDropOptions options,
        ILogger<LiteDbUploadedFileMetadataRepository> logger,
        Action? storageStatsIterationTestHook)
    {
        _logger = logger;
        _storageStatsIterationTestHook = storageStatsIterationTestHook;
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
            // The completion index narrows the eligible set, while the persisted composite key gives LiteDB the
            // complete deterministic ordering before the batch limit is applied.
            _collection.EnsureIndex("sweep_completion", document => document.CompletedAtUnixTimeMilliseconds);
            _collection.EnsureIndex("sweep_order", document => document.SweepOrderKey);
            BackfillSweepOrderKeys();
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Produces a fixed-width ordinal key matching the sweep's required ordering: never-inspected first, then
    /// least-recently-inspected, then completion time, then file identifier. Flipping the sign bit preserves the
    /// full signed timestamp ordering when its hexadecimal representation is compared as text.
    /// </summary>
    internal static String CreateSweepOrderKey(
        Int64? lastSweepAttemptAtUnixTimeMilliseconds,
        Int64? completedAtUnixTimeMilliseconds,
        Guid fileId) =>
        $"{SortableTimestamp(lastSweepAttemptAtUnixTimeMilliseconds)}{SortableTimestamp(completedAtUnixTimeMilliseconds)}{fileId:N}";

    private static Int64 GetReservationCutoffUnixTimeMilliseconds(DateTimeOffset now) =>
        now.Subtract(ReservationRetention).ToUnixTimeMilliseconds();

    private static Boolean IsActiveReservation([NotNullWhen(true)] UploadedFileDocument? document, DateTimeOffset now) =>
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
            document.OwnerCredentialId,
            document.RetentionState);

    private static String SortableTimestamp(Int64? value) =>
        value is { } timestamp
            ? $"1{unchecked((UInt64)(timestamp - Int64.MinValue)).ToString("X16", CultureInfo.InvariantCulture)}"
            : "00000000000000000";

    private void BackfillSweepOrderKeys()
    {
        var documents = _collection.Query()
                                   .Where(document => !document.IsReserved
                                                      && (document.SweepOrderKey == null
                                                          || document.SweepOrderKey == String.Empty))
                                   .ToList();
        foreach (var document in documents)
        {
            document.SweepOrderKey = CreateSweepOrderKey(document.LastSweepAttemptAtUnixTimeMilliseconds,
                                                         document.CompletedAtUnixTimeMilliseconds,
                                                         document.FileId);
            _collection.Update(document);
        }

        if (documents.Count > 0)
        {
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
    }

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
            if (!IsActiveReservation(document, now) || document.OwnerCredentialId != ownerCredentialId)
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

    public Task<IReadOnlyList<UploadedFileListProjection>> GetListProjectionsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        cancellationToken.ThrowIfCancellationRequested();
        if (fileIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<UploadedFileListProjection>>([]);
        }

        IReadOnlyList<UploadedFileListProjection> projections = _collection
                                                                .Find(Query.In("_id", fileIds.Distinct().Select(id => new BsonValue(id))))
                                                                .Where(document => !document.IsReserved)
                                                                .Select(document => new UploadedFileListProjection(document.FileId,
                                                                            document.EncryptedLength,
                                                                            document.RetentionState))
                                                                .ToList();
        return Task.FromResult(projections);
    }

    public Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completedFileCount = 0L;
        var totalEncryptedBytes = 0L;
        foreach (var document in _collection.Find(document => !document.IsReserved))
        {
            _storageStatsIterationTestHook?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (document.RetentionState == BlobRetentionState.Unknown)
            {
                return Task.FromResult(new UploadedFileStorageStats(null, null, false));
            }

            if (document.RetentionState == BlobRetentionState.Retained)
            {
                completedFileCount++;
                totalEncryptedBytes += document.EncryptedLength;
            }
        }

        return Task.FromResult(new UploadedFileStorageStats(completedFileCount, totalEncryptedBytes));
    }

    public Task<IReadOnlyList<UploadSweepCandidate>> GetSweepCandidatesAsync(
        DateTimeOffset completionCutoffUtc,
        Int32 limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<UploadSweepCandidate>>([]);
        }

        var cutoff = completionCutoffUtc.ToUnixTimeMilliseconds();
        lock (_syncRoot)
        {
            // Legacy documents carry no completion timestamp and must be included, or they would never surface;
            // they sort as never-inspected, count against the budget, and are stamped on inspection. The composite
            // key keeps every tie-breaker inside the engine before the limit, so the chosen batch is deterministic.
            var documents = _collection.Query()
                                       .Where(document => !document.IsReserved
                                                          && (document.CompletedAtUnixTimeMilliseconds == null
                                                              || document.CompletedAtUnixTimeMilliseconds <= cutoff))
                                       .OrderBy(document => document.SweepOrderKey)
                                       .Limit(limit)
                                       .ToList();

            IReadOnlyList<UploadSweepCandidate> candidates =
            [
                .. documents
                    .Select(document => new UploadSweepCandidate(
                                document.FileId,
                                document.BlobKey,
                                document.CompletedAtUnixTimeMilliseconds is { } completedAt
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(completedAt)
                                    : null))
            ];
            return Task.FromResult(candidates);
        }
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

            var completed = new UploadedFileDocument
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
                PlaintextSha256 = record.PlaintextSha256,
                RetentionState = BlobRetentionState.Retained,
                CompletedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds()
            };
            completed.SweepOrderKey = CreateSweepOrderKey(null,
                                                          completed.CompletedAtUnixTimeMilliseconds,
                                                          completed.FileId);
            _collection.Update(completed);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            // UploadPersistenceService logs the operator-facing "Upload completed" event with a superset of these fields.
            _logger.LogDebug("Upload reservation completed. FileId: {FileId}; BlobKey: {BlobKey}", record.FileId, record.BlobKey);
            return Task.FromResult(true);
        }
    }

    public Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var document = _collection.FindById(fileId);
            if (document is null)
            {
                return Task.FromResult(true);
            }

            if (document.IsReserved)
            {
                return Task.FromResult(false);
            }

            var deleted = _collection.Delete(fileId);
            if (deleted)
            {
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(deleted);
        }
    }

    public Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var document = _collection.FindById(fileId);
            if (document is null || document.IsReserved)
            {
                return Task.FromResult(false);
            }

            if (document.RetentionState != BlobRetentionState.Deleted)
            {
                document.RetentionState = BlobRetentionState.Deleted;
                _collection.Update(document);
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(true);
        }
    }

    public Task<Boolean> TryRecordSweepInspectionAsync(
        Guid fileId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var document = _collection.FindById(fileId);
            if (document is null || document.IsReserved)
            {
                return Task.FromResult(false);
            }

            var inspectedAt = inspectedAtUtc.ToUnixTimeMilliseconds();
            document.LastSweepAttemptAtUnixTimeMilliseconds = inspectedAt;
            document.CompletedAtUnixTimeMilliseconds ??= inspectedAt;
            document.SweepOrderKey = CreateSweepOrderKey(document.LastSweepAttemptAtUnixTimeMilliseconds,
                                                         document.CompletedAtUnixTimeMilliseconds,
                                                         document.FileId);
            _collection.Update(document);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(true);
        }
    }

    private sealed class UploadedFileDocument
    {
        public String AlgorithmId { get; set; } = String.Empty;

        public String BlobKey { get; set; } = String.Empty;

        public Int64 ChunkCount { get; set; }

        public Int32 ChunkSize { get; set; }

        public Int64? CompletedAtUnixTimeMilliseconds { get; set; }

        public String? ContentType { get; set; }

        public Int64 EncryptedLength { get; set; }

        public String EncryptionFormatVersion { get; set; } = String.Empty;

        [BsonId]
        public Guid FileId { get; set; }

        public Boolean IsClaimed { get; set; }

        public Boolean IsReserved { get; set; }

        public String KdfSaltBase64 { get; set; } = String.Empty;

        public Int64? LastSweepAttemptAtUnixTimeMilliseconds { get; set; }

        public String OriginalFileName { get; set; } = String.Empty;

        public Guid? OwnerCredentialId { get; set; }

        public Int64 PlaintextLength { get; set; }

        public String? PlaintextSha256 { get; set; }

        public Int64? ReservedAtUnixTimeMilliseconds { get; set; }

        public BlobRetentionState RetentionState { get; set; }

        public String? SweepOrderKey { get; set; }
    }
}
