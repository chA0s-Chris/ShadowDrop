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
    IBlobStorage blobStorage,
    TimeProvider timeProvider,
    ILogger<ShareCleanupService> logger)
{
    public async Task<ShareCleanupResult> RunAsync(CancellationToken cancellationToken)
    {
        var candidates = await shareMetadataRepository.GetCleanupCandidatesAsync(timeProvider.GetUtcNow(), cancellationToken);
        var sharesCompleted = 0;
        var blobsDeleted = 0;
        var blobsAlreadyMissing = 0;
        var failures = 0;

        foreach (var share in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var failureCategories = new HashSet<String>(StringComparer.Ordinal);
            foreach (var file in share.Files)
            {
                Boolean? blobDeleted;
                try
                {
                    blobDeleted = await TryCleanupFileAsync(share, file, failureCategories, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Nothing above attributed this failure to a specific cleanup step, so it stays deliberately
                    // unclassified rather than being reported as a provider outage the operator could act on.
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

            var shareFailed = failureCategories.Count > 0;
            var cleanupState = shareFailed ? ShareCleanupState.Failed : ShareCleanupState.Completed;
            var completedAtUtc = timeProvider.GetUtcNow();
            if (!await shareMetadataRepository.TryRecordCleanupAttemptAsync(share.ShareId,
                                                                            cleanupState,
                                                                            completedAtUtc,
                                                                            failureCategories,
                                                                            cancellationToken))
            {
                logger.LogWarning("Share cleanup could not update metadata because the share was missing. ShareId: {ShareId}",
                                  share.ShareId);
                shareFailed = true;
            }

            if (shareFailed)
            {
                failures++;
            }
            else
            {
                sharesCompleted++;
            }
        }

        var result = new ShareCleanupResult(candidates.Count, sharesCompleted, blobsDeleted, blobsAlreadyMissing, failures);
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

        return result;
    }

    /// <summary>
    /// Reports whether a failure is one the cleanup run expects when the metadata provider is temporarily
    /// unreachable, as opposed to a defect that must not be reported to operators as a provider outage.
    /// </summary>
    private static Boolean IsExpectedMetadataFailure(Exception exception) =>
        exception is IOException or TimeoutException or LiteException or MongoException;

    /// <summary>
    /// Deletes one share file's blob and reconciles its retained-blob accounting, recording the sanitized category of
    /// any step that failed.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the blob was deleted, <c>false</c> when it was already missing, and <c>null</c> when the file
    /// was not reached because an earlier step failed.
    /// </returns>
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
            logger.LogWarning("Share cleanup failed because upload metadata was missing. ShareId: {ShareId}; FileId: {FileId}",
                              share.ShareId,
                              file.FileId);
            failureCategories.Add(ShareCleanupFailureCategories.UploadMetadataMissing);
            return null;
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
