// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;

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

            var shareRoutes = adminRoutes.MapGroup("/shares");
            shareRoutes.MapPost("/cleanup", CleanupSharesAsync);
            shareRoutes.MapPost("/{shareId:guid}/revoke", RevokeShareAsync);

            adminRoutes.MapUploadCredentialEndpoints();
        }

        return app;
    }

    private static async Task<IResult> CleanupSharesAsync(ShareCleanupRunner cleanupRunner,
                                                          CancellationToken cancellationToken)
    {
        var result = await cleanupRunner.RunIfIdleAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> RevokeShareAsync(Guid shareId,
                                                        ShareRevocationService shareRevocationService,
                                                        CancellationToken cancellationToken)
    {
        var revoked = await shareRevocationService.RevokeAsync(shareId, cancellationToken);
        return revoked ? Results.NoContent() : Results.NotFound();
    }
}
