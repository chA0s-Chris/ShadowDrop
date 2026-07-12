// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;

public sealed class MongoShareMetadataRepository(IMongoHelper mongo) : IShareMetadataRepository
{
    private IMongoCollection<MongoShareDocument> Collection => mongo.GetCollection<MongoShareDocument>();

    private static MongoShareDocument Map(ShareRecord record) => new()
    {
        ShareId = record.ShareId,
        ShareTokenHashBase64 = record.ShareTokenHashBase64,
        CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
        ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc.ToUnixTimeMilliseconds(),
        RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
        CleanupState = State(record.CleanupState),
        DirectHttpEnabled = record.DirectHttpEnabled,
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
            document.Files.Select(file => new ShareFileEntryRecord(file.FileId, file.OriginalFileName, file.DisplayName)).ToList());

    private static String State(ShareCleanupState state) => state.ToString().ToUpperInvariant();

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

    public async Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var now = nowUtc.ToUniversalTime().ToUnixTimeMilliseconds();

        // Classify every share into exactly one bucket server-side so the counts come from a single,
        // internally consistent read instead of five queries that concurrent writes could skew.
        var groupByBucket = new BsonDocument("$group", new BsonDocument
        {
            ["_id"] = new BsonDocument("$switch", new BsonDocument
            {
                ["branches"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["case"] = new BsonDocument("$eq", new BsonArray
                        {
                            "$CleanupState",
                            State(ShareCleanupState.Completed)
                        }),
                        ["then"] = "completed"
                    },
                    new BsonDocument
                    {
                        ["case"] = new BsonDocument("$eq", new BsonArray
                        {
                            "$CleanupState",
                            State(ShareCleanupState.Failed)
                        }),
                        ["then"] = "failed"
                    },
                    new BsonDocument
                    {
                        ["case"] = new BsonDocument("$gt", new BsonArray
                        {
                            "$RevokedAtUnixTimeMilliseconds",
                            BsonNull.Value
                        }),
                        ["then"] = "revoked"
                    },
                    new BsonDocument
                    {
                        ["case"] = new BsonDocument("$lte", new BsonArray
                        {
                            "$ExpiresAtUnixTimeMilliseconds",
                            now
                        }),
                        ["then"] = "expired"
                    }
                },
                ["default"] = "active"
            }),
            ["count"] = new BsonDocument("$sum", 1)
        });
        PipelineDefinition<MongoShareDocument, BsonDocument> pipeline = new[] { groupByBucket };
        using var cursor = await Collection.AggregateAsync(pipeline, cancellationToken: cancellationToken);
        var counts = (await cursor.ToListAsync(cancellationToken))
            .ToDictionary(bucket => bucket["_id"].AsString, bucket => bucket["count"].ToInt32());
        return new(counts.GetValueOrDefault("active"), counts.GetValueOrDefault("expired"), counts.GetValueOrDefault("revoked"),
                   counts.GetValueOrDefault("completed"), counts.GetValueOrDefault("failed"));
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

    public async Task<Boolean> TryUpdateCleanupStateAsync(Guid shareId, ShareCleanupState cleanupState, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            x => x.ShareId == shareId,
            Builders<MongoShareDocument>.Update.Set(x => x.CleanupState, State(cleanupState)),
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }
}
