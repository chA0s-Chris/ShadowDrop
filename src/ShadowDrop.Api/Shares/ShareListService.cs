// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using Microsoft.Extensions.Primitives;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Globalization;

public sealed class ShareListService
{
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public ShareListService(
        IShareMetadataRepository shareMetadataRepository,
        IUploadedFileMetadataRepository uploadedFileMetadataRepository,
        TimeProvider timeProvider)
    {
        _shareMetadataRepository = shareMetadataRepository;
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _timeProvider = timeProvider;
    }

    public async Task<ShareListPageContract> GetAsync(
        StringValues statusValues,
        StringValues pageSizeValues,
        StringValues cursorValues,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var statuses = NormalizeStatuses(statusValues);
        var pageSize = ParsePageSize(pageSizeValues);
        var cursor = ParseCursor(cursorValues, statuses);
        var query = new ShareListQuery(nowUtc, statuses, pageSize, cursor);

        var page = await _shareMetadataRepository.GetListPageAsync(query, cancellationToken);
        var totalMatching = await _shareMetadataRepository.CountMatchingAsync(query, cancellationToken);
        var fileIds = page.Shares.SelectMany(share => share.FileIds).Distinct().ToArray();
        var files = await _uploadedFileMetadataRepository.GetListProjectionsAsync(fileIds, cancellationToken);
        if (files.Count != fileIds.Length)
        {
            throw new InvalidOperationException("The share-list file metadata projection was incomplete.");
        }

        Dictionary<Guid, UploadedFileListProjection> filesById;
        try
        {
            filesById = files.ToDictionary(file => file.FileId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The share-list file metadata projection was inconsistent.", exception);
        }

        var items = page.Shares.Select(share => Map(share, filesById, nowUtc)).ToArray();
        return new(OperationalStatusProtocol.CurrentVersion, items, page.NextCursor?.Encode(), totalMatching);
    }

    private static ShareListItemContract Map(
        ShareListRecord share,
        IReadOnlyDictionary<Guid, UploadedFileListProjection> files,
        DateTimeOffset nowUtc)
    {
        var retainedBytes = 0L;
        foreach (var fileId in share.FileIds)
        {
            if (!files.TryGetValue(fileId, out var file))
            {
                throw new InvalidOperationException("The share-list file metadata projection was incomplete.");
            }

            if (file.RetentionState == BlobRetentionState.Retained)
            {
                retainedBytes = checked(retainedBytes + file.EncryptedLength);
            }
        }

        return ShareSummaryMapper.Map(share.ShareId,
                                      share.CreatedAtUtc,
                                      share.ExpiresAtUtc,
                                      share.RevokedAtUtc,
                                      share.CleanupState,
                                      share.LastCleanupAttemptAtUtc,
                                      share.CleanupFailureCategories,
                                      share.FileIds.Count,
                                      retainedBytes,
                                      nowUtc);
    }

    private static String[] NormalizeStatuses(StringValues supplied)
    {
        if (supplied.Count == 0)
        {
            return [];
        }

        var values = supplied.ToArray();
        if (values.Any(value => String.IsNullOrEmpty(value)
                                || value.Contains(',', StringComparison.Ordinal)
                                || !ShareListStatuses.CanonicalOrder.Contains(value, StringComparer.Ordinal)))
        {
            throw new OperationalValidationException(OperationalErrorReasons.InvalidRequest);
        }

        var distinct = values.ToHashSet(StringComparer.Ordinal);
        return [.. ShareListStatuses.CanonicalOrder.Where(distinct.Contains)];
    }

    private static ShareListCursor? ParseCursor(StringValues values, IReadOnlyList<String> statuses)
    {
        if (values.Count == 0)
        {
            return null;
        }

        if (values.Count != 1
            || !ShareListCursor.TryDecode(values[0], out var cursor)
            || cursor is null
            || !cursor.Statuses.SequenceEqual(statuses, StringComparer.Ordinal))
        {
            throw new OperationalValidationException(OperationalErrorReasons.InvalidCursor);
        }

        return cursor;
    }

    private static Int32 ParsePageSize(StringValues values)
    {
        if (values.Count == 0)
        {
            return ShareListPagination.DefaultPageSize;
        }

        if (values.Count != 1
            || !Int32.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize)
            || pageSize is <= 0 or > ShareListPagination.MaximumPageSize)
        {
            throw new OperationalValidationException(OperationalErrorReasons.InvalidRequest);
        }

        return pageSize;
    }
}
