// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using LiteDB;
using MongoDB.Driver;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;

public sealed class ShareCleanupService(
    IShareMetadataRepository shareMetadataRepository,
    IUploadedFileMetadataRepository uploadedFileMetadataRepository,
    IShareOperationClaimRepository operationClaimRepository,
    IBlobStorage blobStorage,
    TimeProvider timeProvider,
    ILogger<ShareCleanupService> logger)
{
    public Task<ShareCleanupResult> RunAsync(CancellationToken cancellationToken) =>
        RunAsync(static () => true, cancellationToken);

    internal async Task<ShareCleanupResult> RunAsync(
        Func<Boolean> mayStartWork,
        CancellationToken cancellationToken)
    {
        var candidates = await shareMetadataRepository.GetCleanupCandidatesAsync(timeProvider.GetUtcNow(), cancellationToken);
        var candidatesScanned = 0;
        var sharesCompleted = 0;
        var blobsDeleted = 0;
        var blobsAlreadyMissing = 0;
        var failures = 0;

        foreach (var share in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mayStartWork())
            {
                break;
            }

            candidatesScanned++;
            var claim = await TryAcquireCleanupClaimAsync(share, cancellationToken);
            if (claim is null)
            {
                await RecordFailureAsync(share.ShareId,
                                         [ShareCleanupFailureCategories.Unknown],
                                         cancellationToken);
                failures++;
                continue;
            }

            var failureCategories = new HashSet<String>(StringComparer.Ordinal);
            var interrupted = false;
            foreach (var file in share.Files)
            {
                if (!mayStartWork())
                {
                    interrupted = true;
                    break;
                }

                Boolean? blobDeleted;
                try
                {
                    blobDeleted = await TryCleanupFileAsync(share, file, failureCategories, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception,
                                      "Share cleanup failed for an unclassified reason. ShareId: {ShareId}; FileId: {FileId}",
                                      share.ShareId,
                                      file.FileId);
                    failureCategories.Add(ShareCleanupFailureCategories.Unknown);
                    continue;
                }

                if (blobDeleted is true)
                {
                    blobsDeleted++;
                }
                else if (blobDeleted is false)
                {
                    blobsAlreadyMissing++;
                }
            }

            // Losing the run lease is an orderly hand-off rather than a cleanup failure: the retained claim and
            // the surviving share record keep this share a candidate, so an interrupted share converges on a
            // later run without competing for operator attention with genuinely failing ones. A failure already
            // recorded for an earlier file still counts — the interruption does not make it go away.
            if (interrupted)
            {
                if (failureCategories.Count > 0)
                {
                    await RecordFailureAsync(share.ShareId, failureCategories, cancellationToken);
                    failures++;
                }

                continue;
            }

            if (failureCategories.Count == 0)
            {
                await DeleteUploadedFileMetadataAsync(share, failureCategories, cancellationToken);
            }

            if (failureCategories.Count == 0)
            {
                try
                {
                    if (!await operationClaimRepository.TryReleaseAsync(claim.OperationId, cancellationToken))
                    {
                        failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception,
                                      "Share cleanup could not release its operation claim. ShareId: {ShareId}",
                                      share.ShareId);
                    failureCategories.Add(FailureCategory(exception));
                }
            }

            if (failureCategories.Count == 0)
            {
                try
                {
                    if (!await shareMetadataRepository.TryDeleteAsync(share.ShareId, cancellationToken))
                    {
                        failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception,
                                      "Share cleanup could not delete share metadata. ShareId: {ShareId}",
                                      share.ShareId);
                    failureCategories.Add(FailureCategory(exception));
                }
            }

            if (failureCategories.Count == 0)
            {
                sharesCompleted++;
            }
            else
            {
                await RecordFailureAsync(share.ShareId, failureCategories, cancellationToken);
                failures++;
            }
        }

        var result = new ShareCleanupResult(candidatesScanned,
                                            sharesCompleted,
                                            blobsDeleted,
                                            blobsAlreadyMissing,
                                            failures);
        LogResult(result);
        return result;
    }

    private static String FailureCategory(Exception exception) =>
        IsExpectedMetadataFailure(exception)
            ? ShareCleanupFailureCategories.MetadataUnavailable
            : ShareCleanupFailureCategories.Unknown;

    private static Boolean IsExpectedMetadataFailure(Exception exception) =>
        exception is IOException or TimeoutException or LiteException or MongoException;

    private async Task DeleteUploadedFileMetadataAsync(
        ShareRecord share,
        ISet<String> failureCategories,
        CancellationToken cancellationToken)
    {
        foreach (var file in share.Files)
        {
            try
            {
                if (!await uploadedFileMetadataRepository.TryDeleteAsync(file.FileId, cancellationToken))
                {
                    failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                                  "Share cleanup could not delete uploaded-file metadata. ShareId: {ShareId}; FileId: {FileId}",
                                  share.ShareId,
                                  file.FileId);
                failureCategories.Add(FailureCategory(exception));
                return;
            }
        }
    }

    private void LogResult(ShareCleanupResult result)
    {
        if (result.Failures > 0)
        {
            logger.LogWarning(
                "Share cleanup completed with failures. CandidatesScanned: {CandidatesScanned}; SharesCompleted: {SharesCompleted}; BlobsDeleted: {BlobsDeleted}; BlobsAlreadyMissing: {BlobsAlreadyMissing}; Failures: {Failures}",
                result.CandidatesScanned,
                result.SharesCompleted,
                result.BlobsDeleted,
                result.BlobsAlreadyMissing,
                result.Failures);
        }
        else
        {
            logger.LogInformation(
                "Share cleanup completed. CandidatesScanned: {CandidatesScanned}; SharesCompleted: {SharesCompleted}; BlobsDeleted: {BlobsDeleted}; BlobsAlreadyMissing: {BlobsAlreadyMissing}; Failures: {Failures}",
                result.CandidatesScanned,
                result.SharesCompleted,
                result.BlobsDeleted,
                result.BlobsAlreadyMissing,
                result.Failures);
        }
    }

    private async Task RecordFailureAsync(
        Guid shareId,
        IReadOnlyCollection<String> failureCategories,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await shareMetadataRepository.TryRecordCleanupAttemptAsync(shareId,
                                                                            ShareCleanupState.Failed,
                                                                            timeProvider.GetUtcNow(),
                                                                            failureCategories,
                                                                            cancellationToken))
            {
                logger.LogWarning("Share cleanup could not record its failure because the share was missing. ShareId: {ShareId}",
                                  shareId);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                              "Share cleanup could not record its failure because metadata was unavailable. ShareId: {ShareId}",
                              shareId);
        }
    }

    /// <summary>
    /// Acquires the cleanup claim for <paramref name="share"/>, first releasing a share-creation claim that
    /// its owner never got to release. A committing claim naming a share whose record already exists is
    /// finished work by definition — the record is only inserted after the committing transition wins — so
    /// nothing is left to protect. Without this, a process that died in that window would keep the share's
    /// files claimed forever and no run could ever purge it.
    /// </summary>
    private async Task<ShareOperationClaim?> TryAcquireCleanupClaimAsync(
        ShareRecord share,
        CancellationToken cancellationToken)
    {
        var fileIds = share.Files.Select(file => file.FileId).ToArray();
        var claim = await operationClaimRepository.TryAcquireAsync(share.ShareId,
                                                                   ShareOperationClaimKind.CleanupShare,
                                                                   share.ShareId,
                                                                   fileIds,
                                                                   cancellationToken);
        if (claim is not null)
        {
            return claim;
        }

        var released = false;
        foreach (var abandoned in await operationClaimRepository.GetUnfinishedShareCreationsAsync(fileIds, cancellationToken))
        {
            if (abandoned.ShareId != share.ShareId || abandoned.Lifecycle != ShareOperationClaimLifecycle.Committing)
            {
                continue;
            }

            logger.LogWarning(
                "Share cleanup released an abandoned share-creation claim for a share that already exists. ShareId: {ShareId}; OperationId: {OperationId}",
                share.ShareId,
                abandoned.OperationId);
            released |= await operationClaimRepository.TryReleaseAsync(abandoned.OperationId, cancellationToken);
        }

        return released
            ? await operationClaimRepository.TryAcquireAsync(share.ShareId,
                                                             ShareOperationClaimKind.CleanupShare,
                                                             share.ShareId,
                                                             fileIds,
                                                             cancellationToken)
            : null;
    }

    private async Task<Boolean?> TryCleanupFileAsync(
        ShareRecord share,
        ShareFileEntryRecord file,
        ISet<String> failureCategories,
        CancellationToken cancellationToken)
    {
        UploadedFileRecord? uploadedFile;
        try
        {
            uploadedFile = await uploadedFileMetadataRepository.GetAsync(file.FileId, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedMetadataFailure(exception))
        {
            logger.LogWarning(exception, "Share cleanup failed because upload metadata was unavailable.");
            failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
            return null;
        }

        if (uploadedFile is null)
        {
            return false;
        }

        Boolean blobDeleted;
        try
        {
            blobDeleted = await blobStorage.DeleteIfExistsAsync(uploadedFile.BlobKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                              "Share cleanup failed while deleting a blob. ShareId: {ShareId}; FileId: {FileId}",
                              share.ShareId,
                              file.FileId);
            failureCategories.Add(ShareCleanupFailureCategories.BlobDeleteFailed);
            return null;
        }

        try
        {
            if (!await uploadedFileMetadataRepository.TryMarkBlobDeletedAsync(file.FileId, cancellationToken))
            {
                logger.LogWarning("Share cleanup failed because retained-blob accounting could not be updated.");
                failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
            }
        }
        catch (Exception exception) when (IsExpectedMetadataFailure(exception))
        {
            logger.LogWarning(exception, "Share cleanup failed because retained-blob accounting was unavailable.");
            failureCategories.Add(ShareCleanupFailureCategories.MetadataUnavailable);
        }

        return blobDeleted;
    }
}
