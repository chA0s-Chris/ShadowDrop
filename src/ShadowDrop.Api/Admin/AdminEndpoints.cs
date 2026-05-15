// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnableAdminOperations)
        {
            var adminRoutes = app.MapGroup("/api/admin")
                                 .RequireAdminBearerToken();

            var managementRoutes = adminRoutes.MapGroup("/management");
            managementRoutes.MapGet("/ping", () => Results.Ok(new
            {
                Status = "management-skeleton"
            }));

            var uploadRoutes = adminRoutes.MapGroup("/uploads");
            uploadRoutes.MapPost("/placeholder", () => Results.Ok(new
            {
                Status = "upload-skeleton"
            }));
        }

        return app;
    }
}
