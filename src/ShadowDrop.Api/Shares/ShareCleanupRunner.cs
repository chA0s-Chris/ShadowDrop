// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareCleanupRunner(ShareCleanupService cleanupService, ILogger<ShareCleanupRunner> logger) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<ShareCleanupResult> RunIfIdleAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            logger.LogInformation("Share cleanup skipped because another cleanup run is already in progress.");
            return new(0, 0, 0, 0, 0, Skipped: true);
        }

        try
        {
            return await cleanupService.RunAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
