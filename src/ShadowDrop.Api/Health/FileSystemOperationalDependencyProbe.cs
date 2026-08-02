// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using ShadowDrop.Api.Configuration;

internal sealed class FileSystemOperationalDependencyProbe : IOperationalDependencyProbe
{
    private readonly Action _probe;

    public FileSystemOperationalDependencyProbe(ShadowDropOptions options)
        : this(() =>
        {
            if (!Directory.Exists(options.Storage.LocalRoot))
            {
                throw new DirectoryNotFoundException("The configured storage root is unavailable.");
            }

            _ = Directory.EnumerateFileSystemEntries(options.Storage.LocalRoot).Take(1).ToArray();
        }) { }

    internal FileSystemOperationalDependencyProbe(Action probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public IReadOnlyList<String> Components { get; } = ["storage"];

    public String Name => "filesystem";

    public Task ProbeAsync(CancellationToken cancellationToken) =>
        BlockingOperationalDependencyProbe.RunAsync(_probe, cancellationToken);
}
