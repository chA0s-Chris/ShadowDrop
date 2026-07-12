// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;

internal sealed class MongoShareCleanupCoordinator(IMongoHelper mongo) : IShareCleanupCoordinator, IDisposable
{
    private static readonly TimeSpan DistributedLockLease = TimeSpan.FromMinutes(30);
    private const String DistributedLockName = "shadowdrop-share-cleanup";
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public void Dispose() => _semaphore.Dispose();

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var distributedLock = await mongo.TryAcquireLockAsync(
                DistributedLockName, DistributedLockLease, cancellationToken);
            if (distributedLock is null)
            {
                _semaphore.Release();
                return null;
            }

            return new ShareCleanupCoordinationLease(async () =>
            {
                try
                {
                    await distributedLock.DisposeAsync();
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
}
