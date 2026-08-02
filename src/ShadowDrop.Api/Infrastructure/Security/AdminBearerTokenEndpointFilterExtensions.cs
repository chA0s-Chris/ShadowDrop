// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public static class AdminBearerTokenEndpointFilterExtensions
{
    public static RouteGroupBuilder RequireAdminBearerToken(this RouteGroupBuilder routeGroupBuilder)
    {
        ArgumentNullException.ThrowIfNull(routeGroupBuilder);

        routeGroupBuilder.AddEndpointFilter<AdminBearerTokenEndpointFilter>();

        return routeGroupBuilder;
    }
}
