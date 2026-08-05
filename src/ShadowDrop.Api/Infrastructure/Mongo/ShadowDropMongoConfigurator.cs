// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using Chaos.Mongo;
using Chaos.Mongo.Configuration;
using MongoDB.Driver;

internal sealed class ShadowDropMongoConfigurator : IMongoConfigurator
{
    public async Task ConfigureAsync(IMongoHelper helper, CancellationToken cancellationToken = default)
    {
        var uploads = helper.GetCollection<MongoUploadedFileDocument>();
        await uploads.Indexes.CreateManyAsync([
            new(Builders<MongoUploadedFileDocument>.IndexKeys
                                                   .Ascending(x => x.IsReserved)
                                                   .Ascending(x => x.IsClaimed)
                                                   .Ascending(x => x.ReservedAtUnixTimeMilliseconds),
                new()
                {
                    Name = "reservation_state"
                }),
            new(Builders<MongoUploadedFileDocument>.IndexKeys.Ascending(x => x.IsReserved),
                new()
                {
                    Name = "storage_stats"
                }),
            new(Builders<MongoUploadedFileDocument>.IndexKeys
                                                   .Ascending(x => x.IsReserved)
                                                   .Ascending(x => x.RetentionState),
                new()
                {
                    Name = "retention_stats"
                }),
            // Equality on the reservation flag ahead of the sweep-ordering fields lets candidate selection walk
            // the index in the order the sweep needs, so a large upload collection cannot force a blocking sort.
            new(Builders<MongoUploadedFileDocument>.IndexKeys
                                                   .Ascending(x => x.IsReserved)
                                                   .Ascending(x => x.LastSweepAttemptAtUnixTimeMilliseconds)
                                                   .Ascending(x => x.CompletedAtUnixTimeMilliseconds)
                                                   .Ascending(x => x.FileId),
                new()
                {
                    Name = "unreferenced_upload_sweep"
                })
        ], cancellationToken);

        var shares = helper.GetCollection<MongoShareDocument>();
        await shares.Indexes.CreateManyAsync([
            new(Builders<MongoShareDocument>.IndexKeys.Ascending(x => x.ShareTokenHashBase64),
                new()
                {
                    Name = "share_token_unique",
                    Unique = true
                }),
            new(Builders<MongoShareDocument>.IndexKeys.Ascending("Files.FileId"),
                new()
                {
                    Name = "file_single_use",
                    Unique = true
                }),
            new(Builders<MongoShareDocument>.IndexKeys
                                            .Ascending(x => x.CleanupState)
                                            .Ascending(x => x.ExpiresAtUnixTimeMilliseconds)
                                            .Ascending(x => x.RevokedAtUnixTimeMilliseconds),
                new()
                {
                    Name = "cleanup_candidates"
                }),
            new(Builders<MongoShareDocument>.IndexKeys
                                            .Descending(x => x.CreatedAtUnixTimeMilliseconds)
                                            .Descending(x => x.ShareId),
                new()
                {
                    Name = "newest_first_listing"
                }),
            new(Builders<MongoShareDocument>.IndexKeys.Ascending(x => x.ExpiresAtUnixTimeMilliseconds),
                new()
                {
                    Name = "share_expiration"
                }),
            // Equality on revocation before the expiry range makes the active count a pure COUNT_SCAN. The
            // revocation prefix serves the revoked count with the same plan a single-field index would give,
            // so no separate one is needed.
            new(Builders<MongoShareDocument>.IndexKeys
                                            .Ascending(x => x.RevokedAtUnixTimeMilliseconds)
                                            .Ascending(x => x.ExpiresAtUnixTimeMilliseconds),
                new()
                {
                    Name = "share_lifecycle"
                }),
            new(Builders<MongoShareDocument>.IndexKeys.Ascending(x => x.CleanupState),
                new()
                {
                    Name = "share_cleanup_state"
                })
        ], cancellationToken);

        var operationClaims = helper.GetCollection<MongoShareOperationClaimDocument>();
        await operationClaims.Indexes.CreateManyAsync([
            new(Builders<MongoShareOperationClaimDocument>.IndexKeys.Ascending(x => x.FileIds),
                new()
                {
                    Name = "claimed_file_unique",
                    Unique = true
                }),
            new(Builders<MongoShareOperationClaimDocument>.IndexKeys.Ascending(x => x.Kind),
                new()
                {
                    Name = "claim_kind"
                }),
            // Orphaned-sweep-claim recovery walks its own bounded batch in rotation order. The kind prefix keeps
            // it off the share-creation claims entirely.
            new(Builders<MongoShareOperationClaimDocument>.IndexKeys
                                                          .Ascending(x => x.Kind)
                                                          .Ascending(x => x.LastRecoveryInspectionAtUnixTimeMilliseconds)
                                                          .Ascending(x => x.OperationId),
                new()
                {
                    Name = "sweep_claim_recovery"
                })
        ], cancellationToken);

        // MongoDB's built-in _id index provides the fixed-id admin credential bootstrap guarantee.
        _ = helper.GetCollection<MongoAdminTokenCredentialDocument>();

        var uploadCredentials = helper.GetCollection<MongoUploadCredentialDocument>();
        await uploadCredentials.Indexes.CreateManyAsync([
            new(Builders<MongoUploadCredentialDocument>.IndexKeys.Ascending(x => x.SelectorDigestBase64),
                new()
                {
                    Name = "selector_digest_unique",
                    Unique = true
                }),
            new(Builders<MongoUploadCredentialDocument>.IndexKeys
                                                       .Descending(x => x.CreatedAtUnixTimeMilliseconds)
                                                       .Descending(x => x.CredentialId),
                new()
                {
                    Name = "newest_first_listing"
                })
        ], cancellationToken);
    }
}
