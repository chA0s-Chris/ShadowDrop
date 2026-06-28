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
            document.Files.Select(file => new ShareFileEntryRecord(file.FileId, file.OriginalFileName, file.DisplayName)).ToList());

    public void Dispose() => _database.Dispose();

    public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        _database.BeginTrans();

        try
        {
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
