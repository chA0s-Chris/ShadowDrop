// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using Microsoft.Extensions.Primitives;

public static class AdminBearerTokenEndpointFilterExtensions
{
    public static RouteGroupBuilder RequireAdminBearerToken(this RouteGroupBuilder routeGroupBuilder)
    {
        ArgumentNullException.ThrowIfNull(routeGroupBuilder);

        routeGroupBuilder.AddEndpointFilter(async (invocationContext, next) =>
        {
            var authorizationHeader = invocationContext.HttpContext.Request.Headers.Authorization;
            if (!TryReadBearerToken(authorizationHeader, out var bearerToken))
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

    private static Boolean TryReadBearerToken(StringValues authorizationHeader, out String bearerToken)
    {
        const String bearerPrefix = "Bearer ";
        var headerValue = authorizationHeader.ToString();

        if (!headerValue.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            bearerToken = String.Empty;
            return false;
        }

        bearerToken = headerValue[bearerPrefix.Length..].Trim();
        return !String.IsNullOrWhiteSpace(bearerToken);
    }
}
