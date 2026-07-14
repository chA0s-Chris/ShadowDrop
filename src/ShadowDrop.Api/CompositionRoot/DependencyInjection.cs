// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Chaos.Mongo;
using Serilog;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Downloads;
using ShadowDrop.Api.Health;
using ShadowDrop.Api.Infrastructure.Mongo;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;

public static class DependencyInjection
{
    private const Int64 RequestBodyHeadroomBytes = 16L * 1024 * 1024;

    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder, ILogger logger)
    {
        builder.ConfigureDefaultLogging();

        logger.Information("Configuring services...");
        var shadowDropOptions = ShadowDropOptionsBinding.BindAndValidate(builder.Configuration, builder.Environment.ContentRootPath);

        builder.Services.AddSingleton(shadowDropOptions);
        builder.Services.AddSingleton(TimeProvider.System);

        var mongoRequired = shadowDropOptions.RequiresMongo;
        if (mongoRequired)
        {
            MongoSerialization.EnsureConfigured();
            builder.Services.AddMongo(shadowDropOptions.Mongo.ConnectionString,
                                      shadowDropOptions.Mongo.DatabaseName,
                                      options =>
                                      {
                                          options.UseDefaultCollectionNames = false;
                                          options.RunConfiguratorsOnStartup = false;
                                          options.AddMapping<MongoUploadedFileDocument>("uploaded_files");
                                          options.AddMapping<MongoShareDocument>("shares");
                                          options.AddMapping<MongoAdminTokenCredentialDocument>("admin_tokens");
                                      })
                   .WithConfigurator<ShadowDropMongoConfigurator>();
            builder.Services.AddSingleton<IReadinessCheck, MongoReadinessCheck>();
        }
        else
        {
            builder.Services.AddSingleton<IReadinessCheck, LocalReadinessCheck>();
        }

        // Keep Kestrel's body-size ceiling above the configured upload limit so the reader's friendly UploadPayloadTooLargeException
        // stays authoritative instead of Kestrel aborting the request first.
        var maxRequestBodySize = ResolveMaxRequestBodySize(shadowDropOptions.Upload.MaxBytes);
        builder.WebHost.ConfigureKestrel(kestrelOptions => kestrelOptions.Limits.MaxRequestBodySize = maxRequestBodySize);

        if (shadowDropOptions.ApiExposure.EnableAdminOperations || shadowDropOptions.ApiExposure.EnablePublicDownloads)
        {
            if (shadowDropOptions.Storage.Provider == BlobStorageProvider.FileSystem)
            {
                builder.Services.AddSingleton<IBlobStorage, LocalBlobStorage>();
            }
            else
            {
                builder.Services.AddSingleton<IBlobStorage, MongoGridFsBlobStorage>();
            }

            if (shadowDropOptions.Metadata.Provider == MetadataProvider.LiteDb)
            {
                builder.Services.AddSingleton<IUploadedFileMetadataRepository, LiteDbUploadedFileMetadataRepository>();
                builder.Services.AddSingleton<IShareMetadataRepository, LiteDbShareMetadataRepository>();
            }
            else
            {
                builder.Services.AddSingleton<IUploadedFileMetadataRepository, MongoUploadedFileMetadataRepository>();
                builder.Services.AddSingleton<IShareMetadataRepository, MongoShareMetadataRepository>();
            }

            builder.Services.AddSingleton<ShareCleanupService>();
            if (mongoRequired)
            {
                builder.Services.AddSingleton<IShareCleanupCoordinator, MongoShareCleanupCoordinator>();
            }
            else
            {
                builder.Services.AddSingleton<IShareCleanupCoordinator, InProcessShareCleanupCoordinator>();
            }

            builder.Services.AddSingleton<ShareCleanupRunner>();
            builder.Services.AddHostedService<ShareCleanupHostedService>();
        }

        if (shadowDropOptions.ApiExposure.EnableAdminOperations)
        {
            if (shadowDropOptions.Metadata.Provider == MetadataProvider.LiteDb)
            {
                builder.Services.AddSingleton<IAdminTokenCredentialRepository, LiteDbAdminTokenCredentialRepository>();
            }
            else
            {
                builder.Services.AddSingleton<IAdminTokenCredentialRepository, MongoAdminTokenCredentialRepository>();
            }

            builder.Services.AddSingleton<AdminTokenService>();
            builder.Services.AddSingleton<CreateShareService>();
            builder.Services.AddSingleton<ShareRevocationService>();
            builder.Services.AddSingleton<UploadPersistenceService>();
        }

        if (shadowDropOptions.ApiExposure.EnablePublicDownloads)
        {
            builder.Services.AddSingleton<DownloadFileService>();
        }

        return builder;
    }

    internal static Int64 ResolveMaxRequestBodySize(Int64 maxUploadBytes) =>
        maxUploadBytes > Int64.MaxValue - RequestBodyHeadroomBytes
            ? Int64.MaxValue
            : maxUploadBytes + RequestBodyHeadroomBytes;
}
