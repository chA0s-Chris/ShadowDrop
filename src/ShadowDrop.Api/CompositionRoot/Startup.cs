// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Chaos.Mongo;
using Chaos.Mongo.Configuration;
using MongoDB.Bson;
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
        var mongoRequired = options.Metadata.Provider == MetadataProvider.MongoDb
                            || options.Storage.Provider == BlobStorageProvider.MongoGridFs;
        if (mongoRequired)
        {
            var mongo = app.Services.GetRequiredService<IMongoHelper>();
            await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: CancellationToken.None);
            await app.Services.GetRequiredService<IMongoConfiguratorRunner>().RunConfiguratorsAsync(CancellationToken.None);
        }

        if (options.ApiExposure.EnableAdminOperations)
        {
            await app.Services.GetRequiredService<AdminTokenService>().InitializeAsync(CancellationToken.None);
        }

        LogEffectiveConfiguration(app, logger, options);
        await LogStartupStateSummaryAsync(app, logger, options);

        return app;
    }

    private static void LogEffectiveConfiguration(WebApplication app, ILogger logger, ShadowDropOptions options)
    {
        var maxRequestBodySize = DependencyInjection.ResolveMaxRequestBodySize(options.Upload.MaxBytes);
        logger.Information(
            "Effective configuration: MetadataProvider: {MetadataProvider}; BlobProvider: {BlobProvider}; StorageRoot: {StorageRoot}; " +
            "MetadataDatabase: {MetadataDatabase}; MongoDatabase: {MongoDatabase}; UploadMaxBytes: {UploadMaxBytes}; " +
            "KestrelMaxRequestBodySize: {KestrelMaxRequestBodySize}; EnableAdminOperations: {EnableAdminOperations}; " +
            "EnablePublicDownloads: {EnablePublicDownloads}; CleanupCronExpression: {CleanupCronExpression}",
            options.Metadata.Provider,
            options.Storage.Provider,
            options.Storage.Provider == BlobStorageProvider.FileSystem ? options.Storage.LocalRoot : "(not used)",
            options.Metadata.Provider == MetadataProvider.LiteDb ? options.Metadata.LiteDbPath : "(not used)",
            options.Metadata.Provider == MetadataProvider.MongoDb || options.Storage.Provider == BlobStorageProvider.MongoGridFs
                ? options.Mongo.DatabaseName
                : "(not used)",
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
