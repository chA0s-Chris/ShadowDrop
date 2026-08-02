// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareCleanupRunner
{
    private readonly CleanupRunStatus _cleanupRunStatus;
    private readonly ShareCleanupService _cleanupService;
    private readonly IShareCleanupCoordinator _coordinator;
    private readonly ILogger<ShareCleanupRunner> _logger;
    private readonly TimeProvider _timeProvider;

    public ShareCleanupRunner(ShareCleanupService cleanupService,
                              IShareCleanupCoordinator coordinator,
                              ILogger<ShareCleanupRunner> logger)
        : this(cleanupService, coordinator, TimeProvider.System, new(), logger) { }

    public ShareCleanupRunner(ShareCleanupService cleanupService,
                              IShareCleanupCoordinator coordinator,
                              TimeProvider timeProvider,
                              CleanupRunStatus cleanupRunStatus,
                              ILogger<ShareCleanupRunner> logger)
    {
        _cleanupService = cleanupService;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
        _cleanupRunStatus = cleanupRunStatus;
        _logger = logger;
    }

    public async Task<ShareCleanupResult> RunIfIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lease = await _coordinator.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                _logger.LogInformation("Share cleanup skipped because another cleanup run is already in progress");
                _cleanupRunStatus.Record(_timeProvider.GetUtcNow(), CleanupRunStatus.Skipped);
                return new(0, 0, 0, 0, 0, Skipped: true);
            }

            _logger.LogInformation("Share cleanup started");
            var result = await RunWithLeaseAsync(lease, cancellationToken);
            _cleanupRunStatus.Record(_timeProvider.GetUtcNow(),
                                     result.Failures == 0 ? CleanupRunStatus.Success : CleanupRunStatus.PartialFailure);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _cleanupRunStatus.Record(_timeProvider.GetUtcNow(), CleanupRunStatus.Failure);
            throw;
        }
    }

    private async Task<ShareCleanupResult> RunWithLeaseAsync(IAsyncDisposable lease, CancellationToken cancellationToken)
    {
        await using (lease)
        {
            return await _cleanupService.RunAsync(cancellationToken);
        }
    }
}
