// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Uploads;

public static class DependencyInjection
{
    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder, ILogger logger)
    {
        builder.ConfigureDefaultLogging();

        logger.Information("Configuring services...");
        var shadowDropOptions = ShadowDropOptionsBinding.BindAndValidate(builder.Configuration, builder.Environment.ContentRootPath);

        builder.Services.AddSingleton(shadowDropOptions);

        if (shadowDropOptions.ApiExposure.EnableAdminOperations)
        {
            builder.Services.AddSingleton<AdminTokenService>();
            builder.Services.AddSingleton<IBlobStorage, LocalBlobStorage>();
            builder.Services.AddSingleton<IUploadedFileMetadataRepository, LiteDbUploadedFileMetadataRepository>();
            builder.Services.AddSingleton<UploadPersistenceService>();
        }

        return builder;
    }
}
