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
        var options = app.Services.GetRequiredService<ShadowDropOptions>();

        app.MapHealthEndpoints()
           .MapAdminEndpoints(options)
           .MapDownloadEndpoints(options);

        return app;
    }
}
