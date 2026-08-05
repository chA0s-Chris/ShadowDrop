// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Chaos.Mongo;

internal sealed class MongoShareCleanupCoordinationLease : IShareCleanupCoordinationLease
{
    private readonly IMongoLock _distributedLock;
    private readonly TimeSpan _extensionInterval;
    private readonly Task _extensionTask;
    private readonly TimeSpan _leaseTime;
    private readonly ILogger _logger;
    private readonly Action _releaseLocalLease;
    private readonly TimeSpan _retryInterval;
    private readonly CancellationTokenSource _stopExtension = new();
    private Int32 _disposed;
    private Int32 _ownershipLost;

    public MongoShareCleanupCoordinationLease(
        IMongoLock distributedLock,
        TimeSpan leaseTime,
        TimeSpan extensionInterval,
        TimeSpan retryInterval,
        Action releaseLocalLease,
        ILogger logger)
    {
        _distributedLock = distributedLock;
        _leaseTime = leaseTime;
        _extensionInterval = extensionInterval;
        _retryInterval = retryInterval;
        _releaseLocalLease = releaseLocalLease;
        _logger = logger;
        _extensionTask = ExtendUntilDisposedAsync();
    }

    public Boolean IsValid => Volatile.Read(ref _ownershipLost) == 0 && _distributedLock.IsValid;

    private async Task ExtendUntilDisposedAsync()
    {
        var delay = _extensionInterval;
        while (!_stopExtension.IsCancellationRequested)
        {
            await Task.Delay(delay, _stopExtension.Token);
            if (!_distributedLock.IsValid)
            {
                Interlocked.Exchange(ref _ownershipLost, 1);
                return;
            }

            try
            {
                if (!await _distributedLock.TryExtendAsync(_leaseTime, _stopExtension.Token))
                {
                    Interlocked.Exchange(ref _ownershipLost, 1);
                    _logger.LogWarning("The MongoDB share-cleanup lease was lost while extending it");
                    return;
                }

                delay = _extensionInterval;
            }
            catch (OperationCanceledException) when (_stopExtension.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "The MongoDB share-cleanup lease could not be extended; retrying while it remains valid");
                if (!_distributedLock.IsValid)
                {
                    Interlocked.Exchange(ref _ownershipLost, 1);
                    return;
                }

                delay = _retryInterval;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopExtension.CancelAsync();
        try
        {
            await _extensionTask;
        }
        catch (OperationCanceledException) when (_stopExtension.IsCancellationRequested)
        {
            // Expected while disposing between extension attempts.
        }
        finally
        {
            try
            {
                await _distributedLock.DisposeAsync();
            }
            finally
            {
                _stopExtension.Dispose();
                _releaseLocalLease();
            }
        }
    }
}
