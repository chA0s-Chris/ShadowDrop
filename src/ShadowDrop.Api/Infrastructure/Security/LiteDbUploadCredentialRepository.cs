// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LiteDbUploadCredentialRepository : IUploadCredentialRepository, IDisposable
{
    private readonly ILiteCollection<UploadCredentialDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly Lock _syncRoot = new();

    public LiteDbUploadCredentialRepository(ShadowDropOptions options)
    {
        _databasePath = options.Metadata.LiteDbPath;
        _database = new(new ConnectionString
        {
            Filename = _databasePath,
            Connection = ConnectionType.Shared
        });

        try
        {
            _collection = _database.GetCollection<UploadCredentialDocument>("upload_credentials");
            _collection.EnsureIndex(document => document.CredentialId, true);
            _collection.EnsureIndex(document => document.SelectorDigestBase64, true);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    private static UploadCredentialRecord Map(UploadCredentialDocument document) =>
        new(document.CredentialId,
            document.Name,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.ExpiresAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.RevokedAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.LastUsedAtUnixTimeMilliseconds),
            document.MaxEncryptedFileBytes,
            document.MaxEncryptedShareBytes,
            document.SelectorDigestBase64,
            document.SecretHashBase64,
            document.SecretSaltBase64,
            document.SecretHashIterations,
            document.SecretHashVersion);

    private static DateTimeOffset? ToNullableTimestamp(Int64? unixTimeMilliseconds) =>
        unixTimeMilliseconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds.Value);

    private static UploadCredentialPage ToPage(List<UploadCredentialRecord> fetched, Int32 pageSize)
    {
        if (fetched.Count <= pageSize)
        {
            return new(fetched, null);
        }

        var page = fetched[..pageSize];
        var last = page[^1];
        return new(page, new(last.CreatedAtUtc.ToUnixTimeMilliseconds(), last.CredentialId));
    }

    public void Dispose() => _database.Dispose();

    public Task<UploadCredentialRecord?> FindBySelectorDigestAsync(String selectorDigestBase64, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorDigestBase64);
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindOne(x => x.SelectorDigestBase64 == selectorDigestBase64);
        return Task.FromResult(document is null ? null : Map(document));
    }

    public Task<UploadCredentialRecord?> GetAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = _collection.FindById(credentialId);
        return Task.FromResult(document is null ? null : Map(document));
    }

    public Task<UploadCredentialPage> ListNewestFirstAsync(Int32 pageSize, UploadCredentialListCursor? cursor,
                                                           CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        cancellationToken.ThrowIfCancellationRequested();

        // Credential counts stay small, so ordering in memory is acceptable here; only authentication lookups
        // must avoid scans (see the unique selector-digest index).
        var ordered = _collection.FindAll()
                                 .OrderByDescending(document => document.CreatedAtUnixTimeMilliseconds)
                                 .ThenByDescending(document => document.CredentialId);
        var remaining = cursor is null
            ? ordered
            : ordered.Where(document => document.CreatedAtUnixTimeMilliseconds < cursor.CreatedAtUnixTimeMilliseconds
                                        || (document.CreatedAtUnixTimeMilliseconds == cursor.CreatedAtUnixTimeMilliseconds
                                            && document.CredentialId.CompareTo(cursor.CredentialId) < 0));
        var page = remaining.Take(pageSize + 1).Select(Map).ToList();
        return Task.FromResult(ToPage(page, pageSize));
    }

    public Task RecordUsageAsync(Guid credentialId, DateTimeOffset lastUsedAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // LiteDB has no server-side field-max operator; the guarded read-modify-write under the repository
        // lock provides the same monotonic guarantee without touching revocation state.
        lock (_syncRoot)
        {
            var document = _collection.FindById(credentialId);
            var lastUsedAtUnixTimeMilliseconds = lastUsedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds();
            if (document is null
                || (document.LastUsedAtUnixTimeMilliseconds is { } existing && existing >= lastUsedAtUnixTimeMilliseconds))
            {
                return Task.CompletedTask;
            }

            document.LastUsedAtUnixTimeMilliseconds = lastUsedAtUnixTimeMilliseconds;
            _collection.Update(document);
            return Task.CompletedTask;
        }
    }

    public Task<UploadCredentialRecord?> RevokeAsync(Guid credentialId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var document = _collection.FindById(credentialId);
            if (document is null)
            {
                return Task.FromResult<UploadCredentialRecord?>(null);
            }

            if (document.RevokedAtUnixTimeMilliseconds is null)
            {
                document.RevokedAtUnixTimeMilliseconds = revokedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds();
                _collection.Update(document);
            }

            return Task.FromResult<UploadCredentialRecord?>(Map(document));
        }
    }

    public Task<Boolean> TryCreateAsync(UploadCredentialRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_collection.Exists(x => x.CredentialId == record.CredentialId || x.SelectorDigestBase64 == record.SelectorDigestBase64))
            {
                return Task.FromResult(false);
            }

            _collection.Insert(new UploadCredentialDocument
            {
                CredentialId = record.CredentialId,
                Name = record.Name,
                CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
                ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc?.ToUnixTimeMilliseconds(),
                RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
                LastUsedAtUnixTimeMilliseconds = record.LastUsedAtUtc?.ToUnixTimeMilliseconds(),
                MaxEncryptedFileBytes = record.MaxEncryptedFileBytes,
                MaxEncryptedShareBytes = record.MaxEncryptedShareBytes,
                SelectorDigestBase64 = record.SelectorDigestBase64,
                SecretHashBase64 = record.SecretHashBase64,
                SecretSaltBase64 = record.SecretSaltBase64,
                SecretHashIterations = record.SecretHashIterations,
                SecretHashVersion = record.SecretHashVersion
            });
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(true);
        }
    }

    private sealed class UploadCredentialDocument
    {
        public Int64 CreatedAtUnixTimeMilliseconds { get; set; }

        [BsonId]
        public Guid CredentialId { get; set; }

        public Int64? ExpiresAtUnixTimeMilliseconds { get; set; }

        public Int64? LastUsedAtUnixTimeMilliseconds { get; set; }

        public Int64? MaxEncryptedFileBytes { get; set; }

        public Int64? MaxEncryptedShareBytes { get; set; }

        public String Name { get; set; } = String.Empty;

        public Int64? RevokedAtUnixTimeMilliseconds { get; set; }

        public String SecretHashBase64 { get; set; } = String.Empty;

        public Int32 SecretHashIterations { get; set; }

        public Int32 SecretHashVersion { get; set; }

        public String SecretSaltBase64 { get; set; } = String.Empty;

        public String SelectorDigestBase64 { get; set; } = String.Empty;
    }
}
