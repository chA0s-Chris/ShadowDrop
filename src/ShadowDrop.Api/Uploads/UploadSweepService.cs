// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using System.Security.Cryptography;

/// <summary>
/// Reclaims completed uploads that no share ever referenced. Safety rests on the durable per-file claim and the
/// share-reference re-check taken after it, never on scan ordering: the claim fences share creation out of the
/// file before the sweep looks for references, and it keeps a blob deletion that outlives the cleanup-run lease
/// from racing anything else.
/// </summary>
public sealed class UploadSweepService
{
    internal const Int32 MaxCandidatesPerRun = 200;

    /// <summary>
    /// Orphaned claims arise only from a crash in a narrow window, so a budget well below the candidate budget
    /// keeps a healthy run cheap while still draining a backlog over a few runs.
    /// </summary>
    internal const Int32 MaxRecoveryClaimsPerRun = 50;

    /// <summary>
    /// Namespaces the derivation of a sweep operation identifier from a file identifier, so a retained claim is
    /// reacquired idempotently by a later run and can never collide with an identifier used elsewhere.
    /// </summary>
    private static readonly Guid SweepClaimNamespace = new("6f1a7c2e-9f4b-4a91-8a0d-2b5c7d3e10f4");

    private readonly IBlobStorage _blobStorage;
    private readonly ShareCreationClaimReconciler _claimReconciler;
    private readonly ILogger<UploadSweepService> _logger;
    private readonly IShareOperationClaimRepository _operationClaimRepository;
    private readonly ShadowDropOptions _options;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public UploadSweepService(IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                              IShareMetadataRepository shareMetadataRepository,
                              IShareOperationClaimRepository operationClaimRepository,
                              ShareCreationClaimReconciler claimReconciler,
                              IBlobStorage blobStorage,
                              ShadowDropOptions options,
                              TimeProvider timeProvider,
                              ILogger<UploadSweepService> logger)
    {
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _shareMetadataRepository = shareMetadataRepository;
        _operationClaimRepository = operationClaimRepository;
        _claimReconciler = claimReconciler;
        _blobStorage = blobStorage;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<UploadSweepResult> RunAsync(CancellationToken cancellationToken) =>
        RunAsync(static () => true, cancellationToken);

    internal async Task<UploadSweepResult> RunAsync(Func<Boolean> mayStartWork, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var failures = await RecoverOrphanedClaimsAsync(mayStartWork, now, cancellationToken);
        var cutoff = now - _options.Cleanup.UnreferencedUploadRetention;

        IReadOnlyList<UploadSweepCandidate> candidates;
        try
        {
            candidates = await _uploadedFileMetadataRepository.GetSweepCandidatesAsync(cutoff,
                                                                                       MaxCandidatesPerRun,
                                                                                       cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Unreferenced-upload sweep could not select candidates");
            return Log(new(0, 0, 0, failures + 1));
        }

        var candidatesInspected = 0;
        var uploadsDeleted = 0;
        var blobsAlreadyMissing = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mayStartWork())
            {
                break;
            }

            candidatesInspected++;
            switch (await SweepCandidateAsync(candidate, now, cancellationToken))
            {
                case SweepOutcome.Deleted:
                    uploadsDeleted++;
                    break;
                case SweepOutcome.BlobAlreadyMissing:
                    blobsAlreadyMissing++;
                    break;
                case SweepOutcome.Failed:
                    failures++;
                    break;
            }
        }

        return Log(new(candidatesInspected, uploadsDeleted, blobsAlreadyMissing, failures));
    }

    private static Guid SweepOperationId(Guid fileId)
    {
        Span<Byte> material = stackalloc Byte[32];
        SweepClaimNamespace.TryWriteBytes(material[..16]);
        fileId.TryWriteBytes(material[16..]);
        Span<Byte> digest = stackalloc Byte[SHA256.HashSizeInBytes];
        SHA256.HashData(material, digest);
        return new(digest[..16]);
    }

    /// <summary>
    /// Claims <paramref name="fileId"/> for reclamation, reconciling a conflicting share-creation claim first
    /// because a conflict is not by itself proof that a live creation holds the file. An unresolved conflict
    /// defers the file to a later run rather than doing destructive work or counting a failure.
    /// </summary>
    private async Task<ShareOperationClaim?> AcquireSweepClaimAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var operationId = SweepOperationId(fileId);
        var claim = await _operationClaimRepository.TryAcquireAsync(operationId,
                                                                    ShareOperationClaimKind.SweepUpload,
                                                                    operationId,
                                                                    [fileId],
                                                                    cancellationToken);
        if (claim is not null)
        {
            return claim;
        }

        if (!await _claimReconciler.ReconcileAsync([fileId], cancellationToken))
        {
            return null;
        }

        return await _operationClaimRepository.TryAcquireAsync(operationId,
                                                               ShareOperationClaimKind.SweepUpload,
                                                               operationId,
                                                               [fileId],
                                                               cancellationToken);
    }

    private UploadSweepResult Log(UploadSweepResult result)
    {
        const String message =
            "Unreferenced-upload sweep completed{Qualifier}. CandidatesInspected: {CandidatesInspected}; UploadsDeleted: {UploadsDeleted}; "
            + "BlobsAlreadyMissing: {BlobsAlreadyMissing}; Failures: {Failures}";
        if (result.Failures > 0)
        {
            _logger.LogWarning(message,
                               " with failures",
                               result.CandidatesInspected,
                               result.UploadsDeleted,
                               result.BlobsAlreadyMissing,
                               result.Failures);
        }
        else
        {
            _logger.LogInformation(message,
                                   String.Empty,
                                   result.CandidatesInspected,
                                   result.UploadsDeleted,
                                   result.BlobsAlreadyMissing,
                                   result.Failures);
        }

        return result;
    }

    /// <summary>
    /// Deletes the ciphertext, records the retained-blob accounting transition, and only then removes the
    /// metadata row and releases the claim. Any failure leaves the claim and the row in place, so a later run
    /// retries; a metadata deletion that finds the row already absent is treated as success and converges.
    /// </summary>
    private async Task<SweepOutcome> ReclaimAsync(
        UploadSweepCandidate candidate,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        Boolean blobDeleted;
        try
        {
            blobDeleted = await _blobStorage.DeleteIfExistsAsync(candidate.BlobKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Unreferenced-upload sweep failed while deleting a blob. FailureType: {FailureType}; FileId: {FileId}",
                               exception.GetType().Name,
                               candidate.FileId);
            return SweepOutcome.Failed;
        }

        // Recording the accounting transition before the row disappears is what keeps administrative storage
        // totals from counting ciphertext that is already gone when a later phase fails.
        if (!await _uploadedFileMetadataRepository.TryMarkBlobDeletedAsync(candidate.FileId, cancellationToken))
        {
            _logger.LogWarning("Unreferenced-upload sweep could not update retained-blob accounting. FileId: {FileId}",
                               candidate.FileId);
            return SweepOutcome.Failed;
        }

        if (!await _uploadedFileMetadataRepository.TryDeleteAsync(candidate.FileId, cancellationToken))
        {
            _logger.LogWarning("Unreferenced-upload sweep could not delete upload metadata. FileId: {FileId}",
                               candidate.FileId);
            return SweepOutcome.Failed;
        }

        await _operationClaimRepository.TryReleaseAsync(operationId, cancellationToken);
        return blobDeleted ? SweepOutcome.Deleted : SweepOutcome.BlobAlreadyMissing;
    }

    /// <summary>
    /// Releases sweep claims left behind by a crash between a successful metadata deletion and the claim
    /// release, which deterministic reacquisition alone cannot reach because the deleted upload no longer
    /// appears in the candidate query. Inspected claims rotate, so retained ones cannot hide later orphans.
    /// </summary>
    private async Task<Int32> RecoverOrphanedClaimsAsync(
        Func<Boolean> mayStartWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ShareOperationClaim> claims;
        try
        {
            claims = await _operationClaimRepository.GetSweepClaimsAsync(MaxRecoveryClaimsPerRun, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Unreferenced-upload sweep could not read its claims for recovery");
            return 1;
        }

        var failures = 0;
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mayStartWork())
            {
                break;
            }

            if (claim.FileIds.Count != 1)
            {
                continue;
            }

            var fileId = claim.FileIds[0];
            try
            {
                await _operationClaimRepository.TryRecordSweepClaimInspectionAsync(claim.OperationId, now, cancellationToken);

                // The deletion protocol removes the upload row only after the blob is deleted or confirmed
                // absent, so a claim whose row is gone protects nothing. A claim with a surviving row belongs
                // to the ordinary candidate scheduler.
                if (await _uploadedFileMetadataRepository.GetAsync(fileId, cancellationToken) is not null)
                {
                    continue;
                }

                if (await _operationClaimRepository.TryReleaseAsync(claim.OperationId, cancellationToken))
                {
                    _logger.LogInformation("Unreferenced-upload sweep released an orphaned cleanup claim. FileId: {FileId}",
                                           fileId);
                }
            }
            // Broad by the same reasoning as <see cref="SweepCandidateAsync"/>: one unrecoverable claim costs a
            // failure count, never the rest of the batch.
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Unreferenced-upload sweep could not recover a cleanup claim. FailureType: {FailureType}; FileId: {FileId}",
                                   exception.GetType().Name,
                                   fileId);
                failures++;
            }
        }

        return failures;
    }

    /// <summary>
    /// Sweeps one candidate. Unlike <see cref="ShareCleanupService"/>, which narrows its recovery to the expected
    /// metadata failures, every catch here takes any non-cancellation exception: one unclassified failure must cost
    /// its own candidate and nothing else, because a scheduled reclaimer that aborts a whole run over a single
    /// record stops reclaiming altogether. The failure type is logged without the exception payload so provider
    /// messages cannot disclose blob metadata, and the run is reported as a partial failure.
    /// </summary>
    private async Task<SweepOutcome> SweepCandidateAsync(
        UploadSweepCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            // Rotating the candidate before any decision keeps a record that is skipped or fails on every run
            // from starving fresh ones, and stamps a legacy record so it waits a full grace period from here.
            if (!await _uploadedFileMetadataRepository.TryRecordSweepInspectionAsync(candidate.FileId, now, cancellationToken)
                || candidate.CompletedAtUtc is null)
            {
                return SweepOutcome.Skipped;
            }

            if (await AcquireSweepClaimAsync(candidate.FileId, cancellationToken) is not { } claim)
            {
                return SweepOutcome.Skipped;
            }

            // Only now, behind the claim, is a reference check meaningful: it closes the window in which a share
            // could adopt the file between the check and the deletion.
            if (await _shareMetadataRepository.IsFileReferencedAsync(candidate.FileId, cancellationToken))
            {
                await _operationClaimRepository.TryReleaseAsync(claim.OperationId, cancellationToken);
                return SweepOutcome.Skipped;
            }

            return await ReclaimAsync(candidate, claim.OperationId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Unreferenced-upload sweep failed for an unclassified reason. FailureType: {FailureType}; FileId: {FileId}",
                               exception.GetType().Name,
                               candidate.FileId);
            return SweepOutcome.Failed;
        }
    }

    private enum SweepOutcome
    {
        Skipped,
        Deleted,
        BlobAlreadyMissing,
        Failed
    }
}
