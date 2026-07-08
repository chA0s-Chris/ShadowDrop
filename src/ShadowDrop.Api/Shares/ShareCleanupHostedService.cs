// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Cronos;
using ShadowDrop.Api.Configuration;

public sealed class ShareCleanupHostedService(
    ShareCleanupRunner cleanupRunner,
    ShadowDropOptions options,
    TimeProvider timeProvider,
    ILogger<ShareCleanupHostedService> logger) : BackgroundService
{
    private readonly CronExpression _schedule = CronExpression.Parse(options.Cleanup.CronExpression, CronFormat.Standard);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextOccurrence = _schedule.GetNextOccurrence(now, TimeZoneInfo.Utc);
            if (nextOccurrence is null)
            {
                logger.LogError("Share cleanup schedule produced no future occurrence");
                return;
            }

            var delay = nextOccurrence.Value - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }

            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await cleanupRunner.RunIfIdleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled share cleanup failed");
        }
    }
}
