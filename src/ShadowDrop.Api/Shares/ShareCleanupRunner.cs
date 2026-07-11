// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareCleanupRunner(
    ShareCleanupService cleanupService,
    IShareCleanupCoordinator coordinator,
    ILogger<ShareCleanupRunner> logger)
{
    public async Task<ShareCleanupResult> RunIfIdleAsync(CancellationToken cancellationToken)
    {
        await using var lease = await coordinator.TryAcquireAsync(cancellationToken);
        if (lease is null)
        {
            logger.LogInformation("Share cleanup skipped because another cleanup run is already in progress");
            return new(0, 0, 0, 0, 0, Skipped: true);
        }

        logger.LogInformation("Share cleanup started");
        return await cleanupService.RunAsync(cancellationToken);
    }
}
