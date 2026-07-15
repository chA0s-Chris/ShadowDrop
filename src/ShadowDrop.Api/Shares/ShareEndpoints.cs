// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Security;
using System.Text.Json;

public static class ShareEndpoints
{
    public static WebApplication MapShareEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (!options.ApiExposure.UploadsEnabled)
        {
            return app;
        }

        app.MapGroup("/api/shares")
           .RequireUploadOrAdminBearerToken()
           .MapPost("/", CreateAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(HttpContext httpContext,
                                                   CreateShareService createShareService,
                                                   ILoggerFactory loggerFactory,
                                                   CancellationToken cancellationToken)
    {
        // The request body is read here instead of being bound as a handler parameter so the
        // authentication filter runs before any body processing.
        CreateShareRequest? request = null;
        if (httpContext.Request.HasJsonContentType())
        {
            try
            {
                request = await httpContext.Request.ReadFromJsonAsync<CreateShareRequest>(cancellationToken);
            }
            catch (JsonException) { }
        }

        if (request is null)
        {
            return Results.BadRequest(new
            {
                Error = "Invalid share request."
            });
        }

        try
        {
            var authorizationContext = httpContext.GetUploadAuthorizationContext();
            var result = await createShareService.CreateAsync(request, authorizationContext, cancellationToken);
            return Results.Created($"/api/shares/{result.ShareId}", result);
        }
        catch (CreateShareValidationException exception)
        {
            loggerFactory.CreateLogger(typeof(ShareEndpoints))
                         .LogWarning(exception, "Share request validation failed");
            return Results.BadRequest(new
            {
                Error = "Invalid share request."
            });
        }
    }
}
