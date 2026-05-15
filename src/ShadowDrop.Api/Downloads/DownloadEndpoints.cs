// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using ShadowDrop.Api.Configuration;

public static class DownloadEndpoints
{
    public static WebApplication MapDownloadEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnablePublicDownloads)
        {
            var downloadRoutes = app.MapGroup("/api/downloads");
            downloadRoutes.MapGet(
                "/{shareId:guid}", (Guid shareId) =>
                    Results.Ok(new
                    {
                        Status = "download-skeleton",
                        ShareId = shareId
                    }));
        }

        return app;
    }
}
