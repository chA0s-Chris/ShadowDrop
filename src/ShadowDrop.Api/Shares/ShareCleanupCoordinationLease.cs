// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

internal sealed class ShareCleanupCoordinationLease : IShareCleanupCoordinationLease
{
    private Func<ValueTask>? _release;

    public ShareCleanupCoordinationLease(Func<ValueTask> release)
    {
        _release = release;
    }

    public Boolean IsValid => _release is not null;

    public ValueTask DisposeAsync()
    {
        var releaseOnce = Interlocked.Exchange(ref _release, null);
        return releaseOnce is null ? ValueTask.CompletedTask : releaseOnce();
    }
}
