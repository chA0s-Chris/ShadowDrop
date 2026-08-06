// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;
using ShadowDrop.Contracts;

public sealed class MongoShareMetadataRepository : IShareMetadataRepository
{
    private readonly IMongoHelper _mongo;

    public MongoShareMetadataRepository(IMongoHelper mongo)
    {
        _mongo = mongo;
    }

    private IMongoCollection<MongoShareDocument> Collection => _mongo.GetCollection<MongoShareDocument>();

    private static FilterDefinition<MongoShareDocument> BuildQueryFilter(ShareListQuery query, Boolean includeCursor)
    {
        var builder = Builders<MongoShareDocument>.Filter;
        var now = query.NowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var filter = query.Statuses.Length == 0
            ? builder.Empty
            : builder.Or(query.Statuses.Select(status => BuildStatusFilter(status, now)));
        if (!includeCursor || query.Cursor is null)
        {
            return filter;
        }

        var continuation = builder.Or(
            builder.Lt(x => x.CreatedAtUnixTimeMilliseconds, query.Cursor.CreatedAtUnixTimeMilliseconds),
            builder.And(
                builder.Eq(x => x.CreatedAtUnixTimeMilliseconds, query.Cursor.CreatedAtUnixTimeMilliseconds),
                builder.Lt(x => x.ShareId, query.Cursor.ShareId)));
        return builder.And(filter, continuation);
    }

    private static FilterDefinition<MongoShareDocument> BuildStatusFilter(String status, Int64 now)
    {
        var builder = Builders<MongoShareDocument>.Filter;
        return status switch
        {
            ShareListStatuses.Active => builder.Eq(x => x.RevokedAtUnixTimeMilliseconds, null)
                                        & builder.Gt(x => x.ExpiresAtUnixTimeMilliseconds, now),
            ShareListStatuses.Expired => builder.Lte(x => x.ExpiresAtUnixTimeMilliseconds, now),
            ShareListStatuses.Revoked => builder.Exists(x => x.RevokedAtUnixTimeMilliseconds)
                                         & builder.Ne(x => x.RevokedAtUnixTimeMilliseconds, null),
            ShareListStatuses.CleanupFailed => builder.Eq(x => x.CleanupState, State(ShareCleanupState.Failed)),
            ShareListStatuses.CleanupPending => builder.Ne(x => x.CleanupState, State(ShareCleanupState.Failed)),
            _ => builder.Where(_ => false)
        };
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

    private static MongoShareDocument Map(ShareRecord record) => new()
    {
        ShareId = record.ShareId,
        ShareTokenHashBase64 = record.ShareTokenHashBase64,
        CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
        ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc.ToUnixTimeMilliseconds(),
        RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
        CleanupState = State(record.CleanupState),
        LastCleanupAttemptAtUnixTimeMilliseconds = record.LastCleanupAttemptAtUtc?.ToUnixTimeMilliseconds(),
        CleanupFailureCategories = ShareLifecycle.FailureCategories(record.CleanupFailureCategories).ToList(),
        DirectHttpEnabled = record.DirectHttpEnabled,
        OwnerCredentialId = record.OwnerCredentialId,
        DownloadBearerToken = record.DownloadBearerToken is null
            ? null
            : new()
            {
                TokenHashBase64 = record.DownloadBearerToken.TokenHashBase64,
                ExpiresAtUnixTimeMilliseconds = record.DownloadBearerToken.ExpiresAtUtc.ToUnixTimeMilliseconds()
            },
        Files = record.Files.Select(file => new MongoShareFileEntryDocument
        {
            FileId = file.FileId,
            OriginalFileName = file.OriginalFileName,
            DisplayName = file.DisplayName
        }).ToList()
    };

    private static ShareRecord Map(MongoShareDocument document) =>
        new(document.ShareId,
            document.ShareTokenHashBase64,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(document.ExpiresAtUnixTimeMilliseconds),
            document.RevokedAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.RevokedAtUnixTimeMilliseconds.Value),
            Enum.TryParse<ShareCleanupState>(document.CleanupState, true, out var state) ? state : ShareCleanupState.Pending,
            document.DirectHttpEnabled,
            document.DownloadBearerToken is null
                ? null
                : new(
                    document.DownloadBearerToken.TokenHashBase64,
                    DateTimeOffset.FromUnixTimeMilliseconds(document.DownloadBearerToken.ExpiresAtUnixTimeMilliseconds)),
            document.Files.Select(file => new ShareFileEntryRecord(file.FileId, file.OriginalFileName, file.DisplayName)).ToList(),
            document.OwnerCredentialId,
            document.LastCleanupAttemptAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.LastCleanupAttemptAtUnixTimeMilliseconds.Value),
            ShareLifecycle.FailureCategories(document.CleanupFailureCategories));

    private static ShareListRecord MapList(MongoShareDocument document) =>
        new(document.ShareId,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(document.ExpiresAtUnixTimeMilliseconds),
            document.RevokedAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.RevokedAtUnixTimeMilliseconds.Value),
            Enum.TryParse<ShareCleanupState>(document.CleanupState, true, out var state) ? state : ShareCleanupState.Pending,
            document.LastCleanupAttemptAtUnixTimeMilliseconds is null
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(document.LastCleanupAttemptAtUnixTimeMilliseconds.Value),
            ShareLifecycle.FailureCategories(document.CleanupFailureCategories),
            document.Files.Select(file => file.FileId).ToList());

    private static String State(ShareCleanupState state) => state.ToString().ToUpperInvariant();

    private Task<Int64> CountStatusAsync(String status, Int64 now, CancellationToken cancellationToken) =>
        Collection.CountDocumentsAsync(BuildStatusFilter(status, now), cancellationToken: cancellationToken);

    public Task<Int64> CountMatchingAsync(ShareListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Collection.CountDocumentsAsync(BuildQueryFilter(query, false), cancellationToken: cancellationToken);
    }

    public async Task CreateAsync(ShareRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Files.Select(x => x.FileId).Distinct().Count() != record.Files.Count)
        {
            throw new CreateShareValidationException("A file may only be referenced once by a share.");
        }

        try
        {
            await Collection.InsertOneAsync(Map(record), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await Collection.Find(x => x.ShareId == record.ShareId).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null && Equivalent(Map(existing), record))
            {
                return;
            }

            throw new CreateShareValidationException("The share token or a referenced file is already in use.", exception);
        }
    }

    public async Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.ShareId == shareId).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<ShareRecord?> GetByShareTokenHashAsync(
        String shareTokenHashBase64,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareTokenHashBase64);
        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var document = await Collection.Find(x => x.ShareTokenHashBase64 == shareTokenHashBase64
                                                  && x.RevokedAtUnixTimeMilliseconds == null
                                                  && x.ExpiresAtUnixTimeMilliseconds > now)
                                       .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var documents = await Collection.Find(x => x.ExpiresAtUnixTimeMilliseconds <= now
                                                   || x.RevokedAtUnixTimeMilliseconds != null)
                                        .ToListAsync(cancellationToken);
        return documents.Select(Map).ToList();
    }

    public async Task<ShareListRepositoryPage> GetListPageAsync(ShareListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sort = Builders<MongoShareDocument>.Sort
                                               .Descending(x => x.CreatedAtUnixTimeMilliseconds)
                                               .Descending(x => x.ShareId);
        var projection = Builders<MongoShareDocument>.Projection
                                                     .Include(x => x.ShareId)
                                                     .Include(x => x.CreatedAtUnixTimeMilliseconds)
                                                     .Include(x => x.ExpiresAtUnixTimeMilliseconds)
                                                     .Include(x => x.RevokedAtUnixTimeMilliseconds)
                                                     .Include(x => x.CleanupState)
                                                     .Include(x => x.LastCleanupAttemptAtUnixTimeMilliseconds)
                                                     .Include(x => x.CleanupFailureCategories)
                                                     .Include("Files.FileId");
        var documents = await Collection.Find(BuildQueryFilter(query, true))
                                        .Sort(sort)
                                        .Limit(query.PageSize + 1)
                                        .Project<MongoShareDocument>(projection)
                                        .ToListAsync(cancellationToken);
        var fetched = documents.Select(MapList).ToList();
        if (fetched.Count <= query.PageSize)
        {
            return new(fetched, null);
        }

        var shares = fetched.Take(query.PageSize).ToList();
        var last = shares[^1];
        return new(shares,
                   new(OperationalStatusProtocol.CurrentVersion,
                       query.Statuses,
                       last.CreatedAtUtc.ToUnixTimeMilliseconds(),
                       last.ShareId));
    }

    public async Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var active = CountStatusAsync(ShareListStatuses.Active, now, cancellationToken);
        var expired = CountStatusAsync(ShareListStatuses.Expired, now, cancellationToken);
        var revoked = CountStatusAsync(ShareListStatuses.Revoked, now, cancellationToken);
        var pending = CountStatusAsync(ShareListStatuses.CleanupPending, now, cancellationToken);
        var failed = CountStatusAsync(ShareListStatuses.CleanupFailed, now, cancellationToken);
        await Task.WhenAll(active, expired, revoked, pending, failed);
        return new(await active, await expired, await revoked, await pending, await failed);
    }

    /// <summary>
    /// Served by the same multikey file index that enforces single-use, so the lookup stays a point query
    /// regardless of how many shares exist. No lifecycle predicate is applied: an expired or revoked share
    /// awaiting purge still owns its files.
    /// </summary>
    public Task<Boolean> IsFileReferencedAsync(Guid fileId, CancellationToken cancellationToken) =>
        Collection.Find(Builders<MongoShareDocument>.Filter.Eq("Files.FileId", fileId)).AnyAsync(cancellationToken);

    public async Task<Boolean> TryDeleteAsync(Guid shareId, CancellationToken cancellationToken)
    {
        var result = await Collection.DeleteOneAsync(x => x.ShareId == shareId, cancellationToken);
        if (result.DeletedCount == 1)
        {
            return true;
        }

        return !await Collection.Find(x => x.ShareId == shareId).AnyAsync(cancellationToken);
    }

    public async Task<Boolean> TryRecordCleanupAttemptAsync(
        Guid shareId,
        ShareCleanupState cleanupState,
        DateTimeOffset completedAtUtc,
        IReadOnlyCollection<String> failureCategories,
        CancellationToken cancellationToken)
    {
        var categories = ShareLifecycle.FailureCategories(failureCategories).ToList();
        var result = await Collection.UpdateOneAsync(
            x => x.ShareId == shareId,
            Builders<MongoShareDocument>.Update
                                        .Set(x => x.CleanupState, State(cleanupState))
                                        .Set(x => x.LastCleanupAttemptAtUnixTimeMilliseconds,
                                             completedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds())
                                        .Set(x => x.CleanupFailureCategories, categories),
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            x => x.ShareId == shareId && x.RevokedAtUnixTimeMilliseconds == null,
            Builders<MongoShareDocument>.Update.Set(x => x.RevokedAtUnixTimeMilliseconds,
                                                    revokedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds()),
            cancellationToken: cancellationToken);
        if (result.MatchedCount == 1)
        {
            return true;
        }

        return await Collection.Find(x => x.ShareId == shareId).AnyAsync(cancellationToken);
    }
}
