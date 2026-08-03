// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using Chaos.Mongo;
using MongoDB.Bson;
using ShadowDrop.Api.Configuration;

internal sealed class MongoOperationalDependencyProbe : IOperationalDependencyProbe
{
    private readonly IMongoHelper _mongo;

    public MongoOperationalDependencyProbe(IMongoHelper mongo, ShadowDropOptions options)
    {
        _mongo = mongo;
        Components = BuildComponents(options);
    }

    public IReadOnlyList<String> Components { get; }

    public String Name => "mongo";

    private static List<String> BuildComponents(ShadowDropOptions options)
    {
        var components = new List<String>(2);
        if (options.Metadata.Provider == MetadataProvider.MongoDb)
        {
            components.Add("metadata");
        }

        if (options.Storage.Provider == BlobStorageProvider.MongoGridFs)
        {
            components.Add("storage");
        }

        return components;
    }

    public async Task ProbeAsync(CancellationToken cancellationToken) =>
        _ = await _mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
}
