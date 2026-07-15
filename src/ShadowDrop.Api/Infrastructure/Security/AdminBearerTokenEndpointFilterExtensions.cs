// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public static class AdminBearerTokenEndpointFilterExtensions
{
    public static RouteGroupBuilder RequireAdminBearerToken(this RouteGroupBuilder routeGroupBuilder)
    {
        ArgumentNullException.ThrowIfNull(routeGroupBuilder);

        routeGroupBuilder.AddEndpointFilter(async (invocationContext, next) =>
        {
            var authorizationHeader = invocationContext.HttpContext.Request.Headers.Authorization;
            if (!BearerTokenHeader.TryRead(authorizationHeader, out var bearerToken))
            {
                return Results.Unauthorized();
            }

            var adminTokenService = invocationContext.HttpContext.RequestServices.GetRequiredService<AdminTokenService>();
            if (!adminTokenService.IsValidToken(bearerToken))
            {
                return Results.Unauthorized();
            }

            return await next(invocationContext);
        });

        return routeGroupBuilder;
    }
}
