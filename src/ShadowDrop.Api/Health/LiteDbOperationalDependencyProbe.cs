// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using LiteDB;
using ShadowDrop.Api.Configuration;

internal sealed class LiteDbOperationalDependencyProbe : IOperationalDependencyProbe
{
    private readonly Action _probe;

    public LiteDbOperationalDependencyProbe(ShadowDropOptions options)
        : this(() =>
        {
            using var database = new LiteDatabase(new ConnectionString
            {
                Filename = options.Metadata.LiteDbPath,
                Connection = ConnectionType.Shared,
                ReadOnly = true
            });
            _ = database.GetCollectionNames().Take(1).ToArray();
        }) { }

    internal LiteDbOperationalDependencyProbe(Action probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public IReadOnlyList<String> Components { get; } = ["metadata"];

    public String Name => "litedb";

    public Task ProbeAsync(CancellationToken cancellationToken) =>
        BlockingOperationalDependencyProbe.RunAsync(_probe, cancellationToken);
}
