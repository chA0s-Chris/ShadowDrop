// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;

internal interface IOperationalStatisticsProvider
{
    Task<OperationalStatisticsSnapshot> GetAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

internal sealed record OperationalStatisticsSnapshot(
    UploadedFileStorageStats Storage,
    ShareStatusCounts Shares);

internal sealed class OperationalStatisticsProvider : IOperationalStatisticsProvider
{
    private readonly IShareMetadataRepository _shareRepository;
    private readonly IUploadedFileMetadataRepository _uploadedFileRepository;

    public OperationalStatisticsProvider(
        IUploadedFileMetadataRepository uploadedFileRepository,
        IShareMetadataRepository shareRepository)
    {
        _uploadedFileRepository = uploadedFileRepository;
        _shareRepository = shareRepository;
    }

    public async Task<OperationalStatisticsSnapshot> GetAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var storageTask = _uploadedFileRepository.GetStorageStatsAsync(cancellationToken);
        var sharesTask = _shareRepository.GetStatusCountsAsync(nowUtc, cancellationToken);
        await Task.WhenAll(storageTask, sharesTask);
        return new(await storageTask, await sharesTask);
    }
}
