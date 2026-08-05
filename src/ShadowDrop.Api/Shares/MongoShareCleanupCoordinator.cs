// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;
using Microsoft.Extensions.Logging.Abstractions;

internal sealed class MongoShareCleanupCoordinator : IShareCleanupCoordinator, IDisposable
{
    private static readonly TimeSpan DefaultDistributedLockLease = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultExtensionInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(5);
    private const String DistributedLockName = "shadowdrop-share-cleanup";
    private readonly TimeSpan _distributedLockLease;
    private readonly TimeSpan _extensionInterval;
    private readonly ILogger<MongoShareCleanupCoordinator> _logger;
    private readonly IMongoHelper _mongo;
    private readonly TimeSpan _retryInterval;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public MongoShareCleanupCoordinator(IMongoHelper mongo)
        : this(mongo,
               DefaultDistributedLockLease,
               DefaultExtensionInterval,
               DefaultRetryInterval,
               NullLogger<MongoShareCleanupCoordinator>.Instance) { }

    public MongoShareCleanupCoordinator(IMongoHelper mongo, ILogger<MongoShareCleanupCoordinator> logger)
        : this(mongo,
               DefaultDistributedLockLease,
               DefaultExtensionInterval,
               DefaultRetryInterval,
               logger) { }

    private MongoShareCleanupCoordinator(
        IMongoHelper mongo,
        TimeSpan distributedLockLease,
        TimeSpan extensionInterval,
        TimeSpan retryInterval,
        ILogger<MongoShareCleanupCoordinator> logger)
    {
        _mongo = mongo;
        _distributedLockLease = distributedLockLease;
        _extensionInterval = extensionInterval;
        _retryInterval = retryInterval;
        _logger = logger;
    }

    public void Dispose() => _semaphore.Dispose();

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            var distributedLock = await _mongo.TryAcquireLockAsync(
                DistributedLockName, _distributedLockLease, cancellationToken);
            if (distributedLock is null)
            {
                _semaphore.Release();
                return null;
            }

            return new MongoShareCleanupCoordinationLease(distributedLock,
                                                          _distributedLockLease,
                                                          _extensionInterval,
                                                          _retryInterval,
                                                          () => { _semaphore.Release(); },
                                                          _logger);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }
}
