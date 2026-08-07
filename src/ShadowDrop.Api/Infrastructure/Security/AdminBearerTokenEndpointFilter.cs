// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

internal sealed class AdminBearerTokenEndpointFilter : IEndpointFilter
{
    private readonly AdminTokenService _adminTokenService;

    public AdminBearerTokenEndpointFilter(AdminTokenService adminTokenService)
    {
        _adminTokenService = adminTokenService;
    }

    public async ValueTask<Object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!BearerTokenHeader.TryRead(context.HttpContext.Request.Headers.Authorization, out var bearerToken)
            || !_adminTokenService.IsValidToken(bearerToken))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
