// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Admin;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Downloads;
using ShadowDrop.Api.Health;

public static class Middleware
{
    public static WebApplication ConfigureMiddleware(this WebApplication app, ILogger logger)
    {
        logger.Information("Configuring middleware...");

        var options = app.Services.GetRequiredService<ShadowDropOptions>();

#if ENABLE_THROTTLE_DOWNLOAD
        // DEVELOPMENT-ONLY: paces response bodies so streamed downloads are slow enough to observe the CLI's live progress output.
        app.UseDevelopmentDownloadThrottle(logger);
#endif

        app.MapHealthEndpoints()
           .MapAdminEndpoints(options)
           .MapDownloadEndpoints(options);

        return app;
    }
}
