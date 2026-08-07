// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Chaos.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;

public sealed class MongoUploadedFileMetadataRepository : IUploadedFileMetadataRepository
{
    private static readonly TimeSpan ReservationRetention = TimeSpan.FromDays(1);

    private readonly ILogger<MongoUploadedFileMetadataRepository> _logger;
    private readonly IMongoHelper _mongo;

    public MongoUploadedFileMetadataRepository(IMongoHelper mongo, ILogger<MongoUploadedFileMetadataRepository> logger)
    {
        _mongo = mongo;
        _logger = logger;
    }

    private static Int64 Cutoff => DateTimeOffset.UtcNow.Subtract(ReservationRetention).ToUnixTimeMilliseconds();
    private IMongoCollection<MongoUploadedFileDocument> Collection => _mongo.GetCollection<MongoUploadedFileDocument>();

    private static UploadedFileRecord Map(MongoUploadedFileDocument document) =>
        new(document.FileId, document.BlobKey, document.OriginalFileName, document.PlaintextLength,
            document.EncryptedLength, document.ContentType, document.EncryptionFormatVersion, document.AlgorithmId,
            document.ChunkSize, document.ChunkCount, document.KdfSaltBase64, document.PlaintextSha256,
            document.OwnerCredentialId, document.RetentionState);

    private async Task<Guid> ReserveFileIdAsync(Guid? ownerCredentialId, CancellationToken cancellationToken)
    {
        _ = await Collection.DeleteManyAsync(
            x => x.IsReserved && x.ReservedAtUnixTimeMilliseconds <= Cutoff, cancellationToken);
        while (true)
        {
            var fileId = Guid.NewGuid();
            try
            {
                await Collection.InsertOneAsync(new()
                {
                    FileId = fileId,
                    IsReserved = true,
                    OwnerCredentialId = ownerCredentialId,
                    ReservedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, cancellationToken: cancellationToken);
                return fileId;
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogDebug("Generated upload reservation id collided. FileId: {FileId}", fileId);
            }
        }
    }

    private async Task<Boolean> TryClaimReservationAsync(Guid fileId, Guid? ownerCredentialId, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            x => x.FileId == fileId && x.IsReserved && !x.IsClaimed && x.OwnerCredentialId == ownerCredentialId
                 && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            Builders<MongoUploadedFileDocument>.Update.Set(x => x.IsClaimed, true),
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) =>
        checked((Int32)await Collection.CountDocumentsAsync(
            x => x.IsReserved && !x.IsClaimed && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            cancellationToken: cancellationToken));

    public async Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.FileId == fileId && !x.IsReserved).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<IReadOnlyList<UploadedFileListProjection>> GetListProjectionsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        if (fileIds.Count == 0)
        {
            return [];
        }

        var filter = Builders<MongoUploadedFileDocument>.Filter.In(x => x.FileId, fileIds)
                     & Builders<MongoUploadedFileDocument>.Filter.Eq(x => x.IsReserved, false);
        return await Collection.Find(filter)
                               .Project(x => new UploadedFileListProjection(x.FileId, x.EncryptedLength, x.RetentionState))
                               .ToListAsync(cancellationToken);
    }

    public async Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken)
    {
        var match = new BsonDocument("$match", new BsonDocument("IsReserved", false));
        var group = new BsonDocument("$group", new BsonDocument
        {
            ["_id"] = new BsonDocument("$ifNull", new BsonArray
            {
                "$RetentionState",
                (Int32)BlobRetentionState.Unknown
            }),
            ["count"] = new BsonDocument("$sum", 1),
            ["total"] = new BsonDocument("$sum", "$EncryptedLength")
        });
        PipelineDefinition<MongoUploadedFileDocument, BsonDocument> pipeline = new[]
        {
            match,
            group
        };
        using var cursor = await Collection.AggregateAsync(pipeline, cancellationToken: cancellationToken);
        var groups = await cursor.ToListAsync(cancellationToken);
        if (groups.Any(document => document["_id"].ToInt32() == (Int32)BlobRetentionState.Unknown))
        {
            return new(null, null, false);
        }

        var retained = groups.FirstOrDefault(document => document["_id"].ToInt32() == (Int32)BlobRetentionState.Retained);
        return new(retained?["count"].ToInt64() ?? 0, retained?["total"].ToInt64() ?? 0);
    }

    public async Task<IReadOnlyList<UploadSweepCandidate>> GetSweepCandidatesAsync(
        DateTimeOffset completionCutoffUtc,
        Int32 limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        // A range predicate never matches a missing field, so legacy documents need the explicit null branch or
        // they would never surface. Missing fields sort before numbers, which is exactly the required order:
        // never-inspected first, then least recently inspected, then legacy completion times.
        var builder = Builders<MongoUploadedFileDocument>.Filter;
        var filter = builder.Eq(x => x.IsReserved, false)
                     & builder.Or(builder.Eq(x => x.CompletedAtUnixTimeMilliseconds, null),
                                  builder.Lte(x => x.CompletedAtUnixTimeMilliseconds,
                                              completionCutoffUtc.ToUnixTimeMilliseconds()));
        var sort = Builders<MongoUploadedFileDocument>.Sort
                                                      .Ascending(x => x.LastSweepAttemptAtUnixTimeMilliseconds)
                                                      .Ascending(x => x.CompletedAtUnixTimeMilliseconds)
                                                      .Ascending(x => x.FileId);
        // The timestamp is projected raw and converted here: the driver cannot translate the conversion itself.
        var projections = await Collection.Find(filter)
                                          .Sort(sort)
                                          .Limit(limit)
                                          .Project(x => new SweepCandidateProjection(x.FileId,
                                                                                     x.BlobKey,
                                                                                     x.CompletedAtUnixTimeMilliseconds))
                                          .ToListAsync(cancellationToken);
        return
        [
            .. projections.Select(projection => new UploadSweepCandidate(
                                      projection.FileId,
                                      projection.BlobKey,
                                      projection.CompletedAtUnixTimeMilliseconds is { } completedAt
                                          ? DateTimeOffset.FromUnixTimeMilliseconds(completedAt)
                                          : null))
        ];
    }

    public async Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken)
    {
        // Unlike the LiteDB implementation, an expired claimed reservation is left untouched here;
        // the next ReserveFileIdAsync call prunes it.
        var filter = Builders<MongoUploadedFileDocument>.Filter.Where(x => x.FileId == fileId && x.IsReserved && x.IsClaimed &&
                                                                           x.ReservedAtUnixTimeMilliseconds > Cutoff);
        await Collection.UpdateOneAsync(filter,
                                        Builders<MongoUploadedFileDocument>.Update.Set(x => x.IsClaimed, false),
                                        cancellationToken: cancellationToken);
    }

    public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) =>
        ReserveFileIdAsync(null, cancellationToken);

    public Task<Guid> ReserveFileIdAsync(Guid ownerCredentialId, CancellationToken cancellationToken) =>
        ReserveFileIdAsync((Guid?)ownerCredentialId, cancellationToken);

    public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) =>
        TryClaimReservationAsync(fileId, null, cancellationToken);

    public Task<Boolean> TryClaimReservationAsync(Guid fileId, Guid ownerCredentialId, CancellationToken cancellationToken) =>
        TryClaimReservationAsync(fileId, (Guid?)ownerCredentialId, cancellationToken);

    public async Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var completed = new MongoUploadedFileDocument
        {
            FileId = record.FileId,
            BlobKey = record.BlobKey,
            OriginalFileName = record.OriginalFileName,
            PlaintextLength = record.PlaintextLength,
            EncryptedLength = record.EncryptedLength,
            ContentType = record.ContentType,
            EncryptionFormatVersion = record.EncryptionFormatVersion,
            AlgorithmId = record.AlgorithmId,
            ChunkSize = record.ChunkSize,
            ChunkCount = record.ChunkCount,
            KdfSaltBase64 = record.KdfSaltBase64,
            PlaintextSha256 = record.PlaintextSha256,
            OwnerCredentialId = record.OwnerCredentialId,
            RetentionState = BlobRetentionState.Retained,
            CompletedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        var result = await Collection.ReplaceOneAsync(
            x => x.FileId == record.FileId && x.IsReserved && x.IsClaimed
                 && x.OwnerCredentialId == record.OwnerCredentialId
                 && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            completed,
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var result = await Collection.DeleteOneAsync(x => x.FileId == fileId && !x.IsReserved, cancellationToken);
        if (result.DeletedCount == 1)
        {
            return true;
        }

        return !await Collection.Find(x => x.FileId == fileId).AnyAsync(cancellationToken);
    }

    public async Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            x => x.FileId == fileId && !x.IsReserved && x.RetentionState != BlobRetentionState.Deleted,
            Builders<MongoUploadedFileDocument>.Update.Set(x => x.RetentionState, BlobRetentionState.Deleted),
            cancellationToken: cancellationToken);
        if (result.ModifiedCount == 1)
        {
            return true;
        }

        return await Collection.Find(x => x.FileId == fileId && !x.IsReserved && x.RetentionState == BlobRetentionState.Deleted)
                               .AnyAsync(cancellationToken);
    }

    public async Task<Boolean> TryRecordSweepInspectionAsync(
        Guid fileId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken)
    {
        var inspectedAt = inspectedAtUtc.ToUnixTimeMilliseconds();
        var stamped = await Collection.UpdateOneAsync(
            x => x.FileId == fileId && !x.IsReserved && x.CompletedAtUnixTimeMilliseconds == null,
            Builders<MongoUploadedFileDocument>.Update
                                               .Set(x => x.CompletedAtUnixTimeMilliseconds, inspectedAt)
                                               .Set(x => x.LastSweepAttemptAtUnixTimeMilliseconds, inspectedAt),
            cancellationToken: cancellationToken);
        if (stamped.MatchedCount == 1)
        {
            return true;
        }

        var rotated = await Collection.UpdateOneAsync(
            x => x.FileId == fileId && !x.IsReserved,
            Builders<MongoUploadedFileDocument>.Update.Set(x => x.LastSweepAttemptAtUnixTimeMilliseconds, inspectedAt),
            cancellationToken: cancellationToken);
        return rotated.MatchedCount == 1;
    }

    private sealed record SweepCandidateProjection(Guid FileId, String BlobKey, Int64? CompletedAtUnixTimeMilliseconds);
}
