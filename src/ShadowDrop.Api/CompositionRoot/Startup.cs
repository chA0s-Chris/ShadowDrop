// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;

public static class Startup
{
    public static async Task<WebApplication> PrepareStartupAsync(this WebApplication app, ILogger logger)
    {
        logger.Information("Resolving startup services...");

        var options = app.Services.GetRequiredService<ShadowDropOptions>();
        if (options.ApiExposure.EnableAdminOperations)
        {
            _ = app.Services.GetRequiredService<AdminTokenService>();
        }

        LogEffectiveConfiguration(app, logger, options);
        await LogStartupStateSummaryAsync(app, logger, options);

        return app;
    }

    private static void LogEffectiveConfiguration(WebApplication app, ILogger logger, ShadowDropOptions options)
    {
        var maxRequestBodySize = DependencyInjection.ResolveMaxRequestBodySize(options.Upload.MaxBytes);
        logger.Information(
            "Effective configuration: StorageRoot: {StorageRoot}; MetadataDatabase: {MetadataDatabase}; UploadMaxBytes: {UploadMaxBytes}; " +
            "KestrelMaxRequestBodySize: {KestrelMaxRequestBodySize}; EnableAdminOperations: {EnableAdminOperations}; " +
            "EnablePublicDownloads: {EnablePublicDownloads}; CleanupCronExpression: {CleanupCronExpression}",
            options.Storage.LocalRoot,
            options.Metadata.LiteDbPath,
            options.Upload.MaxBytes,
            maxRequestBodySize,
            options.ApiExposure.EnableAdminOperations,
            options.ApiExposure.EnablePublicDownloads,
            options.Cleanup.CronExpression);
    }

    private static async Task LogStartupStateSummaryAsync(WebApplication app, ILogger logger, ShadowDropOptions options)
    {
        if (!options.ApiExposure.EnableAdminOperations && !options.ApiExposure.EnablePublicDownloads)
        {
            return;
        }

        var uploadedFileRepository = app.Services.GetRequiredService<IUploadedFileMetadataRepository>();
        var shareRepository = app.Services.GetRequiredService<IShareMetadataRepository>();
        var timeProvider = app.Services.GetRequiredService<TimeProvider>();

        var storageStats = await uploadedFileRepository.GetStorageStatsAsync(CancellationToken.None);
        var pendingReservationCount = await uploadedFileRepository.GetActivePendingReservationCountAsync(CancellationToken.None);
        var shareStatusCounts = await shareRepository.GetStatusCountsAsync(timeProvider.GetUtcNow(), CancellationToken.None);

        logger.Information(
            "Startup state summary: CompletedFiles: {CompletedFiles}; StoredBlobBytes: {StoredBlobBytes}; PendingReservations: {PendingReservations}; " +
            "ActiveShares: {ActiveShares}; ExpiredShares: {ExpiredShares}; RevokedShares: {RevokedShares}; CleanupCompletedShares: {CleanupCompletedShares}; " +
            "CleanupFailedShares: {CleanupFailedShares}",
            storageStats.CompletedFileCount,
            storageStats.TotalEncryptedBytes,
            pendingReservationCount,
            shareStatusCounts.Active,
            shareStatusCounts.Expired,
            shareStatusCounts.Revoked,
            shareStatusCounts.CleanupCompleted,
            shareStatusCounts.CleanupFailed);
    }
}
