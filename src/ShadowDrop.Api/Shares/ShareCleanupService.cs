// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Uploads;

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

            var shareFailed = false;
            foreach (var file in share.Files)
            {
                var uploadedFile = await uploadedFileMetadataRepository.GetAsync(file.FileId, cancellationToken);
                if (uploadedFile is null)
                {
                    logger.LogWarning("Share cleanup failed because upload metadata was missing. ShareId: {ShareId}; FileId: {FileId}",
                                      share.ShareId,
                                      file.FileId);
                    shareFailed = true;
                    continue;
                }

                try
                {
                    if (await blobStorage.DeleteIfExistsAsync(uploadedFile.BlobKey, cancellationToken))
                    {
                        blobsDeleted++;
                    }
                    else
                    {
                        blobsAlreadyMissing++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception,
                                      "Share cleanup failed while deleting a blob. ShareId: {ShareId}; FileId: {FileId}",
                                      share.ShareId,
                                      file.FileId);
                    shareFailed = true;
                }
            }

            var cleanupState = shareFailed ? ShareCleanupState.Failed : ShareCleanupState.Completed;
            if (!await shareMetadataRepository.TryUpdateCleanupStateAsync(share.ShareId, cleanupState, cancellationToken))
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
}
