// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Downloads;
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

        // Keep Kestrel's body-size ceiling above the configured upload limit so the reader's friendly UploadPayloadTooLargeException
        // stays authoritative instead of Kestrel aborting the request first.
        var maxRequestBodySize = ResolveMaxRequestBodySize(shadowDropOptions.Upload.MaxBytes);
        builder.WebHost.ConfigureKestrel(kestrelOptions => kestrelOptions.Limits.MaxRequestBodySize = maxRequestBodySize);

        if (shadowDropOptions.ApiExposure.EnableAdminOperations || shadowDropOptions.ApiExposure.EnablePublicDownloads)
        {
            builder.Services.AddSingleton<IBlobStorage, LocalBlobStorage>();
            builder.Services.AddSingleton<IUploadedFileMetadataRepository, LiteDbUploadedFileMetadataRepository>();
            builder.Services.AddSingleton<IShareMetadataRepository, LiteDbShareMetadataRepository>();
            builder.Services.AddSingleton<ShareCleanupService>();
            builder.Services.AddSingleton<ShareCleanupRunner>();
            builder.Services.AddHostedService<ShareCleanupHostedService>();
        }

        if (shadowDropOptions.ApiExposure.EnableAdminOperations)
        {
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
