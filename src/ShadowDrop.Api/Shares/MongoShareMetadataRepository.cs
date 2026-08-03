// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;
using ShadowDrop.Contracts;

public sealed class MongoShareMetadataRepository(IMongoHelper mongo) : IShareMetadataRepository
{
    private IMongoCollection<MongoShareDocument> Collection => mongo.GetCollection<MongoShareDocument>();

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
            ShareListStatuses.CleanupCompleted => builder.Eq(x => x.CleanupState, State(ShareCleanupState.Completed)),
            ShareListStatuses.CleanupPending => builder.Nin(x => x.CleanupState,
                                                            [State(ShareCleanupState.Failed), State(ShareCleanupState.Completed)]),
            _ => builder.Where(_ => false)
        };
    }

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
            throw new CreateShareValidationException("The share token or a referenced file is already in use.", exception);
        }
    }

    public async Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.ShareId == shareId).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareTokenHashBase64);
        var document = await Collection.Find(x => x.ShareTokenHashBase64 == shareTokenHashBase64).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();
        var completed = State(ShareCleanupState.Completed);
        var documents = await Collection.Find(x => x.CleanupState != completed
                                                   && (x.ExpiresAtUnixTimeMilliseconds <= now
                                                       || x.RevokedAtUnixTimeMilliseconds != null))
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
        var active = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.Active, now), cancellationToken: cancellationToken);
        var expired = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.Expired, now), cancellationToken: cancellationToken);
        var revoked = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.Revoked, now), cancellationToken: cancellationToken);
        var pending = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.CleanupPending, now), cancellationToken: cancellationToken);
        var failed = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.CleanupFailed, now), cancellationToken: cancellationToken);
        var completed = await Collection.CountDocumentsAsync(BuildStatusFilter(ShareListStatuses.CleanupCompleted, now), cancellationToken: cancellationToken);
        return new(active, expired, revoked, pending, failed, completed);
    }

    public async Task<Boolean> TryRecordCleanupAttemptAsync(
        Guid shareId,
        ShareCleanupState cleanupState,
        DateTimeOffset completedAtUtc,
        IReadOnlyCollection<String> failureCategories,
        CancellationToken cancellationToken)
    {
        List<String> categories = cleanupState == ShareCleanupState.Completed
            ? []
            : ShareLifecycle.FailureCategories(failureCategories).ToList();
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
