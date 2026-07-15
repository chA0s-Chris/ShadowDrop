// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LiteDbShareMetadataRepository : IShareMetadataRepository, IDisposable
{
    private readonly Action? _afterInsertTestHook;
    private readonly ILiteCollection<ShareDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly Lock _syncRoot = new();

    public LiteDbShareMetadataRepository(ShadowDropOptions options) : this(options, null) { }

    internal LiteDbShareMetadataRepository(ShadowDropOptions options, Action? afterInsertTestHook)
    {
        _afterInsertTestHook = afterInsertTestHook;
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
            _collection = _database.GetCollection<ShareDocument>("shares");
            _collection.EnsureIndex(document => document.ShareId, true);
            _collection.EnsureIndex(document => document.ShareTokenHashBase64, true);
            _collection.EnsureIndex(document => document.Files.Select(file => file.FileId), false);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    private static ShareDocument Map(ShareRecord record) =>
        new()
        {
            ShareId = record.ShareId,
            ShareTokenHashBase64 = record.ShareTokenHashBase64,
            CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
            ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc.ToUnixTimeMilliseconds(),
            RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
            CleanupState = record.CleanupState.ToString().ToUpperInvariant(),
            DirectHttpEnabled = record.DirectHttpEnabled,
            OwnerCredentialId = record.OwnerCredentialId,
            DownloadBearerToken = record.DownloadBearerToken is null
                ? null
                : new DownloadBearerTokenDocument
                {
                    TokenHashBase64 = record.DownloadBearerToken.TokenHashBase64,
                    ExpiresAtUnixTimeMilliseconds = record.DownloadBearerToken.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
            Files = record.Files.Select(file => new ShareFileEntryDocument
            {
                FileId = file.FileId,
                OriginalFileName = file.OriginalFileName,
                DisplayName = file.DisplayName
            }).ToList()
        };

    private static ShareRecord Map(ShareDocument document) =>
        new(document.ShareId,
            document.ShareTokenHashBase64,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(document.ExpiresAtUnixTimeMilliseconds),
            document.RevokedAtUnixTimeMilliseconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(document.RevokedAtUnixTimeMilliseconds.Value),
            Enum.TryParse<ShareCleanupState>(document.CleanupState, true, out var cleanupState) ? cleanupState : ShareCleanupState.Pending,
            document.DirectHttpEnabled,
            document.DownloadBearerToken is null
                ? null
                : new DownloadBearerTokenRecord(
                    document.DownloadBearerToken.TokenHashBase64,
                    DateTimeOffset.FromUnixTimeMilliseconds(document.DownloadBearerToken.ExpiresAtUnixTimeMilliseconds)),
            document.Files.Select(file => new ShareFileEntryRecord(file.FileId, file.OriginalFileName, file.DisplayName)).ToList(),
            document.OwnerCredentialId);

    private Boolean IsFileReferenced(Guid fileId) =>
        _collection.Exists(document => document.Files.Select(file => file.FileId).Any(value => value == fileId));

    public void Dispose() => _database.Dispose();

    public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _database.BeginTrans();

            try
            {
                if (record.Files.Any(file => IsFileReferenced(file.FileId)))
                {
                    throw new CreateShareValidationException("All referenced files must be unused by existing shares.");
                }

                _collection.Insert(Map(record));
                _afterInsertTestHook?.Invoke();
                _database.Commit();
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
                return Task.CompletedTask;
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }
    }

    public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(shareId);
        return Task.FromResult(document is null ? null : Map(document));
    }

    public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareTokenHashBase64);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindOne(share => share.ShareTokenHashBase64 == shareTokenHashBase64);
        return Task.FromResult(document is null ? null : Map(document));
    }

    public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUnixTimeMilliseconds = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var completedState = ShareCleanupState.Completed.ToString().ToUpperInvariant();
        IReadOnlyList<ShareRecord> candidates = _collection
                                                .Find(document => document.CleanupState != completedState
                                                                  && (document.ExpiresAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds
                                                                      || document.RevokedAtUnixTimeMilliseconds != null))
                                                .Select(Map)
                                                .ToList();
        return Task.FromResult(candidates);
    }

    public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUnixTimeMilliseconds = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var completedState = ShareCleanupState.Completed.ToString().ToUpperInvariant();
        var failedState = ShareCleanupState.Failed.ToString().ToUpperInvariant();

        var active = 0;
        var expired = 0;
        var revoked = 0;
        var cleanupCompleted = 0;
        var cleanupFailed = 0;

        foreach (var document in _collection.FindAll())
        {
            if (String.Equals(document.CleanupState, completedState, StringComparison.Ordinal))
            {
                cleanupCompleted++;
            }
            else if (String.Equals(document.CleanupState, failedState, StringComparison.Ordinal))
            {
                cleanupFailed++;
            }
            else if (document.RevokedAtUnixTimeMilliseconds is not null)
            {
                revoked++;
            }
            else if (document.ExpiresAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds)
            {
                expired++;
            }
            else
            {
                active++;
            }
        }

        return Task.FromResult(new ShareStatusCounts(active, expired, revoked, cleanupCompleted, cleanupFailed));
    }

    public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Serialize the read-modify-write so concurrent revocations can't both observe a null
        // RevokedAtUnixTimeMilliseconds and clobber the first caller's timestamp (idempotency guarantee).
        lock (_syncRoot)
        {
            var document = _collection.FindById(shareId);
            if (document is null)
            {
                return Task.FromResult(false);
            }

            if (document.RevokedAtUnixTimeMilliseconds is null)
            {
                document.RevokedAtUnixTimeMilliseconds = revokedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds();
                _collection.Update(document);
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(true);
        }
    }

    public Task<Boolean> TryUpdateCleanupStateAsync(Guid shareId, ShareCleanupState cleanupState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var document = _collection.FindById(shareId);
            if (document is null)
            {
                return Task.FromResult(false);
            }

            var newCleanupState = cleanupState.ToString().ToUpperInvariant();
            if (String.Equals(document.CleanupState, newCleanupState, StringComparison.Ordinal))
            {
                return Task.FromResult(true);
            }

            document.CleanupState = newCleanupState;
            _collection.Update(document);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(true);
        }
    }

    private sealed class DownloadBearerTokenDocument
    {
        public Int64 ExpiresAtUnixTimeMilliseconds { get; set; }

        public String TokenHashBase64 { get; set; } = String.Empty;
    }

    private sealed class ShareDocument
    {
        public String CleanupState { get; set; } = String.Empty;

        public Int64 CreatedAtUnixTimeMilliseconds { get; set; }

        public Boolean DirectHttpEnabled { get; set; }

        public DownloadBearerTokenDocument? DownloadBearerToken { get; set; }

        public Int64 ExpiresAtUnixTimeMilliseconds { get; set; }

        public List<ShareFileEntryDocument> Files { get; set; } = [];

        public Guid? OwnerCredentialId { get; set; }

        public Int64? RevokedAtUnixTimeMilliseconds { get; set; }

        [BsonId]
        public Guid ShareId { get; set; }

        public String ShareTokenHashBase64 { get; set; } = String.Empty;
    }

    private sealed class ShareFileEntryDocument
    {
        public String? DisplayName { get; set; }

        public Guid FileId { get; set; }

        public String OriginalFileName { get; set; } = String.Empty;
    }
}
