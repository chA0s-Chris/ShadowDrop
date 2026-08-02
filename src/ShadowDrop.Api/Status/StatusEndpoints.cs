// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Contracts;

public static class StatusEndpoints
{
    public static WebApplication MapStatusEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        app.MapGet("/api/status", GetPublicStatusAsync).WithName("PublicServerStatus");

        if (options.ApiExposure.UploadsEnabled)
        {
            app.MapGroup("/api/status")
               .RequireScopedUploadStatusBearerToken()
               .MapGet("/upload", GetUploadStatusAsync)
               .WithName("UploadServerStatus");
        }

        return app;
    }

    internal static IResult ToResult(PublicServerStatusContract status) =>
        Results.Json(status,
                     OperationalStatusJsonSerializerContext.Default.PublicServerStatusContract,
                     statusCode: status.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);

    internal static IResult ToResult(UploadServerStatusContract status) =>
        Results.Json(status,
                     OperationalStatusJsonSerializerContext.Default.UploadServerStatusContract,
                     statusCode: status.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);

    internal static IResult ToResult(AdminServerStatusContract status) =>
        Results.Json(status,
                     OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract,
                     statusCode: status.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> GetPublicStatusAsync(OperationalStatusService service, CancellationToken cancellationToken) =>
        ToResult(await service.GetPublicAsync(cancellationToken));

    private static async Task<IResult> GetUploadStatusAsync(
        HttpContext httpContext,
        OperationalStatusService service,
        CancellationToken cancellationToken) =>
        ToResult(await service.GetUploadAsync(httpContext.GetUploadAuthorizationContext(), cancellationToken));
}
