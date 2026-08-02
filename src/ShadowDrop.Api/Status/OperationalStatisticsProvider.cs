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

internal sealed class OperationalStatisticsProvider(
    IUploadedFileMetadataRepository uploadedFileRepository,
    IShareMetadataRepository shareRepository) : IOperationalStatisticsProvider
{
    public async Task<OperationalStatisticsSnapshot> GetAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var storageTask = uploadedFileRepository.GetStorageStatsAsync(cancellationToken);
        var sharesTask = shareRepository.GetStatusCountsAsync(nowUtc, cancellationToken);
        await Task.WhenAll(storageTask, sharesTask);
        return new(await storageTask, await sharesTask);
    }
}
