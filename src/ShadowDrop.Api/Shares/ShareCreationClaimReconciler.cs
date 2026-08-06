// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

/// <summary>
/// Resolves share-creation claims whose owner never got to finish them. Share creation runs this before it
/// claims its own files; the unreferenced-upload sweep runs it when its claim conflicts, because a conflict is
/// not by itself proof that a live share creation holds the file.
/// </summary>
public sealed class ShareCreationClaimReconciler
{
    private readonly ILogger<ShareCreationClaimReconciler> _logger;
    private readonly IShareOperationClaimRepository _operationClaimRepository;
    private readonly IShareMetadataRepository _shareMetadataRepository;

    public ShareCreationClaimReconciler(IShareOperationClaimRepository operationClaimRepository,
                                        IShareMetadataRepository shareMetadataRepository,
                                        ILogger<ShareCreationClaimReconciler> logger)
    {
        _operationClaimRepository = operationClaimRepository;
        _shareMetadataRepository = shareMetadataRepository;
        _logger = logger;
    }

    /// <summary>
    /// Aborts every still-acquired creation claim over <paramref name="fileIds"/> and finishes every committing
    /// one by retrying its persisted proposed-share insertion. The conditional lifecycle is what makes this safe
    /// against a live owner: an owner that has already won the committing transition is never aborted, and one
    /// that has not yet won it fails its own transition afterwards rather than inserting a share over files this
    /// reconciliation released.
    /// </summary>
    /// <returns><see langword="true"/> when at least one claim was released, so a caller may retry its own acquisition.</returns>
    public async Task<Boolean> ReconcileAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileIds);

        var reconciled = false;
        foreach (var claim in await _operationClaimRepository.GetUnfinishedShareCreationsAsync(fileIds, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claim.Lifecycle == ShareOperationClaimLifecycle.Acquired)
            {
                reconciled |= await _operationClaimRepository.TryAbortAcquiredAsync(claim.OperationId, cancellationToken);
                continue;
            }

            if (claim.ProposedShare is null)
            {
                continue;
            }

            try
            {
                await _shareMetadataRepository.CreateAsync(claim.ProposedShare, cancellationToken);
                reconciled |= await _operationClaimRepository.TryReleaseAsync(claim.OperationId, cancellationToken);
            }
            catch (CreateShareValidationException exception)
            {
                _logger.LogWarning(exception,
                                   "Abandoned share creation failed definitively during recovery. ShareId: {ShareId}; OperationId: {OperationId}",
                                   claim.ShareId,
                                   claim.OperationId);
                reconciled |= await _operationClaimRepository.TryReleaseAsync(claim.OperationId, cancellationToken);
            }
        }

        return reconciled;
    }
}
