// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;
using ShadowDrop.Contracts;

public sealed class LiteDbShareMetadataRepository : IShareMetadataRepository, IDisposable
{
    private readonly Action? _afterInsertTestHook;
    private readonly ILiteCollection<ShareDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly Action? _statusStatsIterationTestHook;
    private readonly Lock _syncRoot = new();

    public LiteDbShareMetadataRepository(ShadowDropOptions options) : this(options, null, null) { }

    internal LiteDbShareMetadataRepository(ShadowDropOptions options, Action? afterInsertTestHook) : this(options, afterInsertTestHook, null) { }

    internal LiteDbShareMetadataRepository(
        ShadowDropOptions options,
        Action? afterInsertTestHook,
        Action? statusStatsIterationTestHook)
    {
        _afterInsertTestHook = afterInsertTestHook;
        _statusStatsIterationTestHook = statusStatsIterationTestHook;
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
            _collection.EnsureIndex(document => document.Files.Select(file => file.FileId));
            _collection.EnsureIndex(document => document.CreatedAtUnixTimeMilliseconds);
            _collection.EnsureIndex(document => document.ExpiresAtUnixTimeMilliseconds);
            _collection.EnsureIndex(document => document.RevokedAtUnixTimeMilliseconds);
            _collection.EnsureIndex(document => document.CleanupState);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    private static Boolean Equivalent(ShareRecord left, ShareRecord right) =>
        left.ShareId == right.ShareId
        && String.Equals(left.ShareTokenHashBase64, right.ShareTokenHashBase64, StringComparison.Ordinal)
        && left.CreatedAtUtc.ToUnixTimeMilliseconds() == right.CreatedAtUtc.ToUnixTimeMilliseconds()
        && left.ExpiresAtUtc.ToUnixTimeMilliseconds() == right.ExpiresAtUtc.ToUnixTimeMilliseconds()
        && left.RevokedAtUtc?.ToUnixTimeMilliseconds() == right.RevokedAtUtc?.ToUnixTimeMilliseconds()
        && left.CleanupState == right.CleanupState
        && left.DirectHttpEnabled == right.DirectHttpEnabled
        && Equivalent(left.DownloadBearerToken, right.DownloadBearerToken)
        && left.OwnerCredentialId == right.OwnerCredentialId
        && left.Files.SequenceEqual(right.Files);

    private static Boolean Equivalent(DownloadBearerTokenRecord? left, DownloadBearerTokenRecord? right) =>
        left is null
            ? right is null
            : right is not null
              && String.Equals(left.TokenHashBase64, right.TokenHashBase64, StringComparison.Ordinal)
              && left.ExpiresAtUtc.ToUnixTimeMilliseconds() == right.ExpiresAtUtc.ToUnixTimeMilliseconds();

    private static ShareDocument Map(ShareRecord record) =>
        new()
        {
            ShareId = record.ShareId,
            ShareTokenHashBase64 = record.ShareTokenHashBase64,
            CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
            ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc.ToUnixTimeMilliseconds(),
            RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
            CleanupState = record.CleanupState.ToString().ToUpperInvariant(),
            LastCleanupAttemptAtUnixTimeMilliseconds = record.LastCleanupAttemptAtUtc?.ToUnixTimeMilliseconds(),
            CleanupFailureCategories = ShareLifecycle.FailureCategories(record.CleanupFailureCategories).ToList(),
            DirectHttpEnabled = record.DirectHttpEnabled,
            OwnerCredentialId = record.OwnerCredentialId,
            DownloadBearerToken = record.DownloadBearerToken is null
                ? null
                : new DownloadBearerTokenDocument
                {
                    TokenHashBase64 = record.DownloadBearerToken.TokenHashBase64,
                    ExpiresAtUnixTimeMilliseconds = record.DownloadBearerToken.ExpiresAtUtc.ToUnixTimeMilliseconds()
                },
            Files =
            [
                .. record.Files.Select(file => new ShareFileEntryDocument
                {
                    FileId = file.FileId,
                    OriginalFileName = file.OriginalFileName,
                    DisplayName = file.DisplayName
                })
            ]
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
            document.OwnerCredentialId,
            document.LastCleanupAttemptAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.LastCleanupAttemptAtUnixTimeMilliseconds.Value),
            ShareLifecycle.FailureCategories(document.CleanupFailureCategories));

    private static ShareListRecord MapList(ShareDocument document) =>
        new(document.ShareId,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(document.ExpiresAtUnixTimeMilliseconds),
            document.RevokedAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.RevokedAtUnixTimeMilliseconds.Value),
            ParseCleanupState(document.CleanupState),
            document.LastCleanupAttemptAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.LastCleanupAttemptAtUnixTimeMilliseconds.Value),
            ShareLifecycle.FailureCategories(document.CleanupFailureCategories),
            document.Files.Select(file => file.FileId).ToList());

    private static ShareCleanupState ParseCleanupState(String? value) =>
        Enum.TryParse<ShareCleanupState>(value, true, out var state) ? state : ShareCleanupState.Pending;

    private ILiteQueryable<ShareDocument> CreateListQuery(
        ShareListQuery query,
        Int64? exactCreatedAt = null,
        Int64? createdBefore = null,
        Int64? createdAtOrBefore = null,
        Guid? shareIdBefore = null)
    {
        var parameters = new List<BsonValue>();
        var clauses = new List<String>();

        String Parameter(BsonValue value)
        {
            var name = $"@{parameters.Count}";
            parameters.Add(value);
            return name;
        }

        if (query.Statuses.Length > 0)
        {
            var now = Parameter(query.NowUtc.ToUniversalTime().ToUnixTimeMilliseconds());
            var failed = Parameter(nameof(ShareCleanupState.Failed).ToUpperInvariant());
            var statusClauses = query.Statuses.Select(status => status switch
            {
                ShareListStatuses.Active => $"($.RevokedAtUnixTimeMilliseconds = null AND $.ExpiresAtUnixTimeMilliseconds > {now})",
                ShareListStatuses.Expired => $"$.ExpiresAtUnixTimeMilliseconds <= {now}",
                ShareListStatuses.Revoked => "$.RevokedAtUnixTimeMilliseconds != null",
                ShareListStatuses.CleanupFailed => $"$.CleanupState = {failed}",
                ShareListStatuses.CleanupPending => $"$.CleanupState != {failed}",
                _ => "false"
            });
            clauses.Add($"({String.Join(" OR ", statusClauses)})");
        }

        if (exactCreatedAt is not null)
        {
            clauses.Add($"$.CreatedAtUnixTimeMilliseconds = {Parameter(exactCreatedAt.Value)}");
        }

        if (createdBefore is not null)
        {
            clauses.Add($"$.CreatedAtUnixTimeMilliseconds < {Parameter(createdBefore.Value)}");
        }

        if (createdAtOrBefore is not null)
        {
            clauses.Add($"$.CreatedAtUnixTimeMilliseconds <= {Parameter(createdAtOrBefore.Value)}");
        }

        if (shareIdBefore is not null)
        {
            clauses.Add($"$._id < {Parameter(shareIdBefore.Value)}");
        }

        var result = _collection.Query();
        return clauses.Count == 0
            ? result
            : result.Where(String.Join(" AND ", clauses), parameters.ToArray());
    }

    /// <summary>
    /// Reads the next descending run of matching creation timestamps in one indexed query.
    /// </summary>
    /// <remarks>
    /// LiteDB orders by a single field, so the page is still assembled one equal-timestamp group at a time. Fetching
    /// the timestamps as one bounded batch keeps that to a single ordering query per page instead of one per group:
    /// with a status filter the optimizer drops off the creation-time index, which made the per-group form cost a
    /// full collection scan for every row of the page.
    /// </remarks>
    private List<Int64> FindCreatedAtBatch(ShareListQuery query, Int64? bound, Boolean inclusive, Int32 limit) =>
        CreateListQuery(query,
                        createdBefore: inclusive ? null : bound,
                        createdAtOrBefore: inclusive ? bound : null)
            .OrderByDescending(document => document.CreatedAtUnixTimeMilliseconds)
            .Select(document => document.CreatedAtUnixTimeMilliseconds)
            .Limit(limit)
            .ToList();

    private Boolean IsFileReferenced(Guid fileId) =>
        _collection.Exists(document => document.Files.Select(file => file.FileId).Any(value => value == fileId));

    public void Dispose() => _database.Dispose();

    public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((Int64)CreateListQuery(query).Count());
    }

    public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _database.BeginTrans();

            try
            {
                var existing = _collection.FindById(record.ShareId);
                if (existing is not null)
                {
                    if (Equivalent(Map(existing), record))
                    {
                        _database.Commit();
                        return Task.CompletedTask;
                    }

                    throw new CreateShareValidationException("The share identifier is already in use.");
                }

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

    public Task<ShareRecord?> GetByShareTokenHashAsync(
        String shareTokenHashBase64,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareTokenHashBase64);
        cancellationToken.ThrowIfCancellationRequested();

        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var document = _collection.FindOne(share => share.ShareTokenHashBase64 == shareTokenHashBase64
                                                    && share.RevokedAtUnixTimeMilliseconds == null
                                                    && share.ExpiresAtUnixTimeMilliseconds > now);
        return Task.FromResult(document is null ? null : Map(document));
    }

    public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUnixTimeMilliseconds = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        IReadOnlyList<ShareRecord> candidates = _collection
                                                .Find(document => document.ExpiresAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds
                                                                  || document.RevokedAtUnixTimeMilliseconds != null)
                                                .Select(Map)
                                                .ToList();
        return Task.FromResult(candidates);
    }

    public Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var wanted = query.PageSize + 1;
        var fetched = new List<ShareListRecord>(wanted);
        var cursorCreatedAt = query.Cursor?.CreatedAtUnixTimeMilliseconds;
        var bound = cursorCreatedAt;
        var inclusive = query.Cursor is not null;
        while (fetched.Count < wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The page holds at most pageSize + 1 shares, so every creation timestamp it can still need is within
            // the next pageSize + 1 matching timestamps. One query therefore replaces the whole group walk.
            var timestamps = FindCreatedAtBatch(query, bound, inclusive, wanted - fetched.Count);
            if (timestamps.Count == 0)
            {
                break;
            }

            Int64? previous = null;
            foreach (var createdAt in timestamps)
            {
                if (createdAt == previous)
                {
                    continue;
                }

                previous = createdAt;
                cancellationToken.ThrowIfCancellationRequested();

                // The tie-break stays inside LiteDB: within one timestamp the identifier index orders the group and
                // the cursor's identifier excludes everything already returned, so nothing is materialized twice.
                var group = CreateListQuery(query,
                                            createdAt,
                                            shareIdBefore: createdAt == cursorCreatedAt ? query.Cursor?.ShareId : null)
                            .OrderByDescending(document => document.ShareId)
                            .Limit(wanted - fetched.Count)
                            .ToList();
                fetched.AddRange(group.Select(MapList));
                if (fetched.Count >= wanted)
                {
                    break;
                }
            }

            // A partially consumed cursor group can yield fewer shares than timestamps, so continue strictly after
            // the last timestamp inspected rather than assuming the batch was enough.
            bound = previous;
            inclusive = false;
        }

        if (fetched.Count <= query.PageSize)
        {
            return Task.FromResult(new ShareListRepositoryPage(fetched, null));
        }

        var shares = fetched.Take(query.PageSize).ToList();
        var last = shares[^1];
        var cursor = new ShareListCursor(OperationalStatusProtocol.CurrentVersion,
                                         query.Statuses,
                                         last.CreatedAtUtc.ToUnixTimeMilliseconds(),
                                         last.ShareId);
        return Task.FromResult(new ShareListRepositoryPage(shares, cursor));
    }

    public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _statusStatsIterationTestHook?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        Int64 Count(String status)
        {
            return CreateListQuery(new(nowUtc, [status], 1, null)).Count();
        }

        return Task.FromResult(new ShareStatusCounts(Count(ShareListStatuses.Active),
                                                     Count(ShareListStatuses.Expired),
                                                     Count(ShareListStatuses.Revoked),
                                                     Count(ShareListStatuses.CleanupPending),
                                                     Count(ShareListStatuses.CleanupFailed)));
    }

    public Task<Boolean> IsFileReferencedAsync(Guid fileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(IsFileReferenced(fileId));
        }
    }

    public Task<Boolean> TryDeleteAsync(Guid shareId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var deleted = _collection.Delete(shareId);
            if (deleted)
            {
                FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            }

            return Task.FromResult(deleted || !_collection.Exists(document => document.ShareId == shareId));
        }
    }

    public Task<Boolean> TryRecordCleanupAttemptAsync(
        Guid shareId,
        ShareCleanupState cleanupState,
        DateTimeOffset completedAtUtc,
        IReadOnlyCollection<String> failureCategories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var document = _collection.FindById(shareId);
            if (document is null)
            {
                return Task.FromResult(false);
            }

            document.CleanupState = cleanupState.ToString().ToUpperInvariant();
            document.LastCleanupAttemptAtUnixTimeMilliseconds = completedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds();
            document.CleanupFailureCategories = ShareLifecycle.FailureCategories(failureCategories).ToList();
            _collection.Update(document);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(true);
        }
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
        public List<String> CleanupFailureCategories { get; set; } = [];

        public String CleanupState { get; set; } = String.Empty;

        public Int64 CreatedAtUnixTimeMilliseconds { get; set; }

        public Boolean DirectHttpEnabled { get; set; }

        public DownloadBearerTokenDocument? DownloadBearerToken { get; set; }

        public Int64 ExpiresAtUnixTimeMilliseconds { get; set; }

        public List<ShareFileEntryDocument> Files { get; set; } = [];

        public Int64? LastCleanupAttemptAtUnixTimeMilliseconds { get; set; }

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
