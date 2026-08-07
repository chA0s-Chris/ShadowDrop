// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Globalization;

public sealed class ShareInspectionService
{
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public ShareInspectionService(
        IShareMetadataRepository shareMetadataRepository,
        IUploadedFileMetadataRepository uploadedFileMetadataRepository,
        TimeProvider timeProvider)
    {
        _shareMetadataRepository = shareMetadataRepository;
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _timeProvider = timeProvider;
    }

    public async Task<ShareInspectionContract?> GetAsync(
        Guid shareId,
        Boolean includeFilenames,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var share = await _shareMetadataRepository.GetAsync(shareId, cancellationToken);
        if (share is null)
        {
            return null;
        }

        var requestedFileIds = share.Files.Select(file => file.FileId).Distinct().ToArray();
        var requestedFileIdSet = requestedFileIds.ToHashSet();
        var projections = await _uploadedFileMetadataRepository.GetListProjectionsAsync(requestedFileIds, cancellationToken);
        Dictionary<Guid, UploadedFileListProjection> projectionsById;
        try
        {
            projectionsById = projections.ToDictionary(projection => projection.FileId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The share-inspection file metadata projection was inconsistent.", exception);
        }

        if (projectionsById.Keys.Any(fileId => !requestedFileIdSet.Contains(fileId)))
        {
            throw new InvalidOperationException("The share-inspection file metadata projection was inconsistent.");
        }

        var retainedBytes = 0L;
        var files = new ShareInspectionFileContract[share.Files.Count];
        for (var index = 0; index < share.Files.Count; index++)
        {
            var shareFile = share.Files[index];
            var ciphertextBytes = 0L;
            String retentionState;
            if (!projectionsById.TryGetValue(shareFile.FileId, out var projection))
            {
                retentionState = ShareFileRetentionStates.Missing;
            }
            else
            {
                retentionState = MapRetentionState(projection.RetentionState);
                if (projection.RetentionState == BlobRetentionState.Retained)
                {
                    ciphertextBytes = projection.EncryptedLength;
                    retainedBytes = checked(retainedBytes + ciphertextBytes);
                }
            }

            files[index] = new(shareFile.FileId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant(),
                               ciphertextBytes,
                               retentionState,
                               includeFilenames ? shareFile.OriginalFileName : null,
                               includeFilenames ? shareFile.DisplayName : null);
        }

        var summary = ShareSummaryMapper.Map(share.ShareId,
                                             share.CreatedAtUtc,
                                             share.ExpiresAtUtc,
                                             share.RevokedAtUtc,
                                             share.CleanupState,
                                             share.LastCleanupAttemptAtUtc,
                                             share.CleanupFailureCategories,
                                             share.Files.Count,
                                             retainedBytes,
                                             nowUtc);
        return new(OperationalStatusProtocol.CurrentVersion,
                   summary.ShareId,
                   summary.CreatedAtUtc,
                   summary.ExpiresAtUtc,
                   summary.RevokedAtUtc,
                   summary.Statuses,
                   summary.CleanupState,
                   summary.LastCleanupAttemptAtUtc,
                   summary.CleanupFailureCategories,
                   summary.FileCount,
                   summary.CiphertextBytes,
                   files);
    }

    private static String MapRetentionState(BlobRetentionState state) => state switch
    {
        BlobRetentionState.Retained => ShareFileRetentionStates.Retained,
        BlobRetentionState.Deleted => ShareFileRetentionStates.Deleted,
        BlobRetentionState.Unknown => ShareFileRetentionStates.Unknown,
        _ => throw new InvalidOperationException("The share-inspection retention state was inconsistent.")
    };
}
