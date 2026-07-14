// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using Chaos.Mongo;
using MongoDB.Bson;

internal sealed class MongoReadinessCheck(IMongoHelper mongo) : IReadinessCheck
{
    internal static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(3);

    internal TimeSpan CheckTimeout { get; init; } = DefaultCheckTimeout;

    public async Task<Boolean> IsReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
