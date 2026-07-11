// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

internal sealed class ShareCleanupCoordinationLease(Func<ValueTask> release) : IAsyncDisposable
{
    private Func<ValueTask>? _release = release;

    public ValueTask DisposeAsync()
    {
        var releaseOnce = Interlocked.Exchange(ref _release, null);
        return releaseOnce is null ? ValueTask.CompletedTask : releaseOnce();
    }
}
