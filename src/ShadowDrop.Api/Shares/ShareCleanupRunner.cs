// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Uploads;

public sealed class ShareCleanupRunner
{
    private readonly CleanupRunStatus _cleanupRunStatus;
    private readonly ShareCleanupService _cleanupService;
    private readonly IShareCleanupCoordinator _coordinator;
    private readonly ILogger<ShareCleanupRunner> _logger;
    private readonly UploadSweepService _sweepService;
    private readonly TimeProvider _timeProvider;

    public ShareCleanupRunner(ShareCleanupService cleanupService,
                              UploadSweepService sweepService,
                              IShareCleanupCoordinator coordinator,
                              ILogger<ShareCleanupRunner> logger)
        : this(cleanupService, sweepService, coordinator, TimeProvider.System, new(), logger) { }

    public ShareCleanupRunner(ShareCleanupService cleanupService,
                              UploadSweepService sweepService,
                              IShareCleanupCoordinator coordinator,
                              TimeProvider timeProvider,
                              CleanupRunStatus cleanupRunStatus,
                              ILogger<ShareCleanupRunner> logger)
    {
        _cleanupService = cleanupService;
        _sweepService = sweepService;
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

    /// <summary>
    /// Runs the share phase and then the unreferenced-upload sweep under the same lease and lease-validity
    /// callback. The sweep runs second so the share phase can converge partially failed purges first and the
    /// candidate query sees the freshest state; ordering carries no correctness weight, because the sweep's own
    /// durable per-file claims are what make it safe.
    /// </summary>
    private async Task<ShareCleanupResult> RunWithLeaseAsync(IAsyncDisposable lease, CancellationToken cancellationToken)
    {
        await using (lease)
        {
            Boolean MayStartWork()
            {
                return lease is not IShareCleanupCoordinationLease coordinationLease || coordinationLease.IsValid;
            }

            var shareResult = await _cleanupService.RunAsync(MayStartWork, cancellationToken);
            var sweepResult = await _sweepService.RunAsync(MayStartWork, cancellationToken);
            return shareResult with
            {
                Failures = shareResult.Failures + sweepResult.Failures,
                SweepCandidatesInspected = sweepResult.CandidatesInspected,
                SweepUploadsDeleted = sweepResult.UploadsDeleted,
                SweepBlobsAlreadyMissing = sweepResult.BlobsAlreadyMissing,
                SweepFailures = sweepResult.Failures
            };
        }
    }
}
