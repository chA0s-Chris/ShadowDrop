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
                })
        ], cancellationToken);

        // MongoDB's built-in _id index provides the fixed-id admin credential bootstrap guarantee.
        _ = helper.GetCollection<MongoAdminTokenCredentialDocument>();
    }
}
