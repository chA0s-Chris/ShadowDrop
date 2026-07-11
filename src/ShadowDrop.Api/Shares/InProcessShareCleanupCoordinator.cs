// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

internal sealed class InProcessShareCleanupCoordinator : IShareCleanupCoordinator, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Dispose() => _semaphore.Dispose();

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        return new ShareCleanupCoordinationLease(() =>
        {
            _semaphore.Release();
            return ValueTask.CompletedTask;
        });
    }
}
