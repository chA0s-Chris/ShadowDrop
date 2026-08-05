// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;
using JsonSerializer = System.Text.Json.JsonSerializer;

public sealed class LiteDbShareOperationClaimRepository : IShareOperationClaimRepository, IDisposable
{
    private readonly ILiteCollection<ClaimDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly Lock _syncRoot = new();

    public LiteDbShareOperationClaimRepository(ShadowDropOptions options)
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
            _collection = _database.GetCollection<ClaimDocument>("share_operation_claims");
            _collection.EnsureIndex(document => document.OperationId, true);
            _collection.EnsureIndex(document => document.FileIds);
            _collection.EnsureIndex(document => document.Kind);
            _collection.EnsureIndex("sweep_recovery", document => document.LastRecoveryInspectionAtUnixTimeMilliseconds);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    private static ShareOperationClaim Map(ClaimDocument document) =>
        new(document.OperationId,
            document.Kind,
            document.ShareId,
            document.FileIds,
            document.Lifecycle,
            document.ProposedShareJson is null
                ? null
                : JsonSerializer.Deserialize<ShareRecord>(document.ProposedShareJson));

    private static Boolean Matches(
        ClaimDocument document,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds) =>
        document.Kind == kind
        && document.ShareId == shareId
        && document.FileIds.Order().SequenceEqual(fileIds);

    private Task<Boolean> TryDeleteAsync(
        Guid operationId,
        ShareOperationClaimLifecycle lifecycle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _database.BeginTrans();
            try
            {
                var document = _collection.FindById(operationId);
                if (document is null || document.Lifecycle != lifecycle)
                {
                    _database.Commit();
                    return Task.FromResult(false);
                }

                var deleted = _collection.Delete(operationId);
                _database.Commit();
                if (deleted)
                {
                    FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
                }

                return Task.FromResult(deleted);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
    }

    public void Dispose() => _database.Dispose();

    public Task<IReadOnlyList<ShareOperationClaim>> GetSweepClaimsAsync(Int32 limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<ShareOperationClaim>>([]);
        }

        lock (_syncRoot)
        {
            IReadOnlyList<ShareOperationClaim> claims =
            [
                .. _collection.Find(document => document.Kind == ShareOperationClaimKind.SweepUpload)
                              .OrderBy(document => document.LastRecoveryInspectionAtUnixTimeMilliseconds.HasValue)
                              .ThenBy(document => document.LastRecoveryInspectionAtUnixTimeMilliseconds ?? 0)
                              .ThenBy(document => document.OperationId)
                              .Take(limit)
                              .Select(Map)
            ];
            return Task.FromResult(claims);
        }
    }

    public Task<IReadOnlyList<ShareOperationClaim>> GetUnfinishedShareCreationsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matches = new Dictionary<Guid, ShareOperationClaim>();
        lock (_syncRoot)
        {
            // One indexed lookup per requested file: LiteDB cannot translate a set membership test over a
            // document array, and a share's file list is small enough that the repeated lookups stay cheaper
            // than scanning every claim.
            foreach (var fileId in fileIds.Distinct())
            {
                foreach (var document in _collection.Find(document => document.FileIds.Contains(fileId))
                                                    .Where(document => document.Kind == ShareOperationClaimKind.CreateShare))
                {
                    matches[document.OperationId] = Map(document);
                }
            }
        }

        IReadOnlyList<ShareOperationClaim> claims = [.. matches.Values];
        return Task.FromResult(claims);
    }

    public Task<Boolean> TryAbortAcquiredAsync(Guid operationId, CancellationToken cancellationToken) =>
        TryDeleteAsync(operationId, ShareOperationClaimLifecycle.Acquired, cancellationToken);

    public Task<ShareOperationClaim?> TryAcquireAsync(
        Guid operationId,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedFileIds = fileIds.Distinct().Order().ToArray();
        lock (_syncRoot)
        {
            _database.BeginTrans();
            try
            {
                var existing = _collection.FindById(operationId);
                if (existing is not null)
                {
                    _database.Commit();
                    return Task.FromResult(Matches(existing, kind, shareId, normalizedFileIds)
                                               ? Map(existing)
                                               : null);
                }

                if (normalizedFileIds.Any(fileId => _collection.Exists(document => document.FileIds.Contains(fileId))))
                {
                    _database.Commit();
                    return Task.FromResult<ShareOperationClaim?>(null);
                }

                var document = new ClaimDocument
                {
                    OperationId = operationId,
                    Kind = kind,
                    ShareId = shareId,
                    FileIds = normalizedFileIds.ToList(),
                    Lifecycle = ShareOperationClaimLifecycle.Acquired
                };
                try
                {
                    _collection.Insert(document);
                }
                catch (LiteException exception) when (exception.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
                {
                    _database.Rollback();
                    return Task.FromResult<ShareOperationClaim?>(null);
                }

                _database.Commit();
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
                return Task.FromResult<ShareOperationClaim?>(Map(document));
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
    }

    public Task<Boolean> TryBeginCommitAsync(
        Guid operationId,
        ShareRecord proposedShare,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _database.BeginTrans();
            try
            {
                var document = _collection.FindById(operationId);
                if (document is null
                    || document.Kind != ShareOperationClaimKind.CreateShare
                    || document.Lifecycle != ShareOperationClaimLifecycle.Acquired
                    || document.ShareId != proposedShare.ShareId)
                {
                    _database.Commit();
                    return Task.FromResult(false);
                }

                document.ProposedShareJson = JsonSerializer.Serialize(proposedShare);
                document.Lifecycle = ShareOperationClaimLifecycle.Committing;
                var updated = _collection.Update(document);
                _database.Commit();
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
                return Task.FromResult(updated);
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
    }

    public Task<Boolean> TryRecordSweepClaimInspectionAsync(
        Guid operationId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var document = _collection.FindById(operationId);
            if (document is null || document.Kind != ShareOperationClaimKind.SweepUpload)
            {
                return Task.FromResult(false);
            }

            document.LastRecoveryInspectionAtUnixTimeMilliseconds = inspectedAtUtc.ToUnixTimeMilliseconds();
            var updated = _collection.Update(document);
            if (updated)
            {
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(updated);
        }
    }

    public Task<Boolean> TryReleaseAsync(Guid operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var deleted = _collection.Delete(operationId);
            if (deleted)
            {
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(deleted);
        }
    }

    private sealed class ClaimDocument
    {
        public List<Guid> FileIds { get; set; } = [];

        public ShareOperationClaimKind Kind { get; set; }

        public Int64? LastRecoveryInspectionAtUnixTimeMilliseconds { get; set; }

        public ShareOperationClaimLifecycle Lifecycle { get; set; }

        [BsonId]
        public Guid OperationId { get; set; }

        public String? ProposedShareJson { get; set; }

        public Guid ShareId { get; set; }
    }
}
