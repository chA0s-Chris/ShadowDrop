// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;

public sealed class MongoUploadedFileMetadataRepository(IMongoHelper mongo, ILogger<MongoUploadedFileMetadataRepository> logger)
    : IUploadedFileMetadataRepository
{
    private static readonly TimeSpan ReservationRetention = TimeSpan.FromDays(1);

    private static Int64 Cutoff => DateTimeOffset.UtcNow.Subtract(ReservationRetention).ToUnixTimeMilliseconds();
    private IMongoCollection<MongoUploadedFileDocument> Collection => mongo.GetCollection<MongoUploadedFileDocument>();

    private static UploadedFileRecord Map(MongoUploadedFileDocument document) =>
        new(document.FileId, document.BlobKey, document.OriginalFileName, document.PlaintextLength,
            document.EncryptedLength, document.ContentType, document.EncryptionFormatVersion, document.AlgorithmId,
            document.ChunkSize, document.ChunkCount, document.KdfSaltBase64, document.PlaintextSha256);

    public async Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken) =>
        checked((Int32)await Collection.CountDocumentsAsync(
            x => x.IsReserved && !x.IsClaimed && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            cancellationToken: cancellationToken));

    public async Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.FileId == fileId && !x.IsReserved).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken)
    {
        var stats = await Collection.Aggregate()
                                    .Match(x => !x.IsReserved)
                                    .Group(_ => 1, group => new
                                    {
                                        Count = group.Count(),
                                        Total = group.Sum(x => x.EncryptedLength)
                                    })
                                    .FirstOrDefaultAsync(cancellationToken);
        return new(stats?.Count ?? 0, stats?.Total ?? 0);
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

    public async Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken)
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
                    ReservedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, cancellationToken: cancellationToken);
                return fileId;
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                logger.LogDebug("Generated upload reservation id collided. FileId: {FileId}", fileId);
            }
        }
    }

    public async Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            x => x.FileId == fileId && x.IsReserved && !x.IsClaimed && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            Builders<MongoUploadedFileDocument>.Update.Set(x => x.IsClaimed, true),
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

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
            PlaintextSha256 = record.PlaintextSha256
        };
        var result = await Collection.ReplaceOneAsync(
            x => x.FileId == record.FileId && x.IsReserved && x.IsClaimed && x.ReservedAtUnixTimeMilliseconds > Cutoff,
            completed,
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }
}
