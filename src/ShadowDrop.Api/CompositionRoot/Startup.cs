// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;

public static class Startup
{
    public static WebApplication PrepareStartup(this WebApplication app, ILogger logger)
    {
        logger.Information("Resolving startup services...");

        var options = app.Services.GetRequiredService<ShadowDropOptions>();
        if (options.ApiExposure.EnableAdminOperations)
        {
            _ = app.Services.GetRequiredService<AdminTokenService>();
        }

        return app;
    }
}
