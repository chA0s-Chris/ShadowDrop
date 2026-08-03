// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

internal interface IOperationalDependencyProbe
{
    IReadOnlyList<String> Components { get; }

    String Name { get; }

    Task ProbeAsync(CancellationToken cancellationToken);
}

internal static class BlockingOperationalDependencyProbe
{
    public static async Task RunAsync(Action probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();

        var probeTask = Task.Run(probe, CancellationToken.None);
        _ = probeTask.ContinueWith(static completed => _ = completed.Exception,
                                   CancellationToken.None,
                                   TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                                   TaskScheduler.Default);
        await probeTask.WaitAsync(cancellationToken);
    }
}
