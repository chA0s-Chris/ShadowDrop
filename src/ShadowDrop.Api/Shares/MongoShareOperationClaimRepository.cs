// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;
using System.Text.Json;

public sealed class MongoShareOperationClaimRepository : IShareOperationClaimRepository
{
    private readonly IMongoHelper _mongo;

    public MongoShareOperationClaimRepository(IMongoHelper mongo)
    {
        _mongo = mongo;
    }

    private IMongoCollection<MongoShareOperationClaimDocument> Collection =>
        _mongo.GetCollection<MongoShareOperationClaimDocument>();

    private static ShareOperationClaim Map(MongoShareOperationClaimDocument document) =>
        new(document.OperationId,
            document.Kind,
            document.ShareId,
            document.FileIds,
            document.Lifecycle,
            document.ProposedShareJson is null
                ? null
                : JsonSerializer.Deserialize<ShareRecord>(document.ProposedShareJson));

    private static Boolean Matches(
        MongoShareOperationClaimDocument document,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds) =>
        document.Kind == kind
        && document.ShareId == shareId
        && document.FileIds.Order().SequenceEqual(fileIds);

    public async Task<IReadOnlyList<ShareOperationClaim>> GetSweepClaimsAsync(Int32 limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        // A missing inspection timestamp sorts before every number, so never-inspected claims come first.
        var sort = Builders<MongoShareOperationClaimDocument>.Sort
                                                             .Ascending(x => x.LastRecoveryInspectionAtUnixTimeMilliseconds)
                                                             .Ascending(x => x.OperationId);
        var documents = await Collection
                              .Find(document => document.Kind == ShareOperationClaimKind.SweepUpload)
                              .Sort(sort)
                              .Limit(limit)
                              .ToListAsync(cancellationToken);
        return [.. documents.Select(Map)];
    }

    public async Task<IReadOnlyList<ShareOperationClaim>> GetUnfinishedShareCreationsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        var builder = Builders<MongoShareOperationClaimDocument>.Filter;
        var filter = builder.Eq(document => document.Kind, ShareOperationClaimKind.CreateShare)
                     & builder.AnyIn(document => document.FileIds, fileIds.Distinct());
        var documents = await Collection.Find(filter).ToListAsync(cancellationToken);
        return [.. documents.Select(Map)];
    }

    public async Task<Boolean> TryAbortAcquiredAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var result = await Collection.DeleteOneAsync(
            document => document.OperationId == operationId
                        && document.Lifecycle == ShareOperationClaimLifecycle.Acquired,
            cancellationToken);
        return result.DeletedCount == 1;
    }

    public async Task<ShareOperationClaim?> TryAcquireAsync(
        Guid operationId,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        var normalizedFileIds = fileIds.Distinct().Order().ToList();
        var document = new MongoShareOperationClaimDocument
        {
            OperationId = operationId,
            Kind = kind,
            ShareId = shareId,
            FileIds = normalizedFileIds,
            Lifecycle = ShareOperationClaimLifecycle.Acquired
        };
        try
        {
            await Collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            return Map(document);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await Collection.Find(claim => claim.OperationId == operationId)
                                           .FirstOrDefaultAsync(cancellationToken);
            return existing is not null && Matches(existing, kind, shareId, normalizedFileIds) ? Map(existing) : null;
        }
    }

    public async Task<Boolean> TryBeginCommitAsync(
        Guid operationId,
        ShareRecord proposedShare,
        CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            document => document.OperationId == operationId
                        && document.Kind == ShareOperationClaimKind.CreateShare
                        && document.Lifecycle == ShareOperationClaimLifecycle.Acquired
                        && document.ShareId == proposedShare.ShareId,
            Builders<MongoShareOperationClaimDocument>.Update
                                                      .Set(document => document.ProposedShareJson,
                                                           JsonSerializer.Serialize(proposedShare))
                                                      .Set(document => document.Lifecycle,
                                                           ShareOperationClaimLifecycle.Committing),
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<Boolean> TryRecordSweepClaimInspectionAsync(
        Guid operationId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await Collection.UpdateOneAsync(
            document => document.OperationId == operationId && document.Kind == ShareOperationClaimKind.SweepUpload,
            Builders<MongoShareOperationClaimDocument>.Update
                                                      .Set(document => document.LastRecoveryInspectionAtUnixTimeMilliseconds,
                                                           inspectedAtUtc.ToUnixTimeMilliseconds()),
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<Boolean> TryReleaseAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var result = await Collection.DeleteOneAsync(document => document.OperationId == operationId, cancellationToken);
        return result.DeletedCount == 1;
    }
}
