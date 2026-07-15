// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public static class UploadOrAdminBearerTokenEndpointFilterExtensions
{
    private static readonly Object AuthorizationContextKey = new();

    public static UploadCredentialAuthorizationContext GetUploadAuthorizationContext(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(AuthorizationContextKey, out var value)
               && value is UploadCredentialAuthorizationContext authorizationContext
            ? authorizationContext
            : throw new InvalidOperationException("The upload authorization context is unavailable.");
    }

    public static RouteGroupBuilder RequireUploadOrAdminBearerToken(this RouteGroupBuilder routeGroupBuilder)
    {
        ArgumentNullException.ThrowIfNull(routeGroupBuilder);

        routeGroupBuilder.AddEndpointFilter(async (invocationContext, next) =>
        {
            if (!BearerTokenHeader.TryRead(invocationContext.HttpContext.Request.Headers.Authorization, out var bearerToken))
            {
                return Results.Unauthorized();
            }

            UploadCredentialAuthorizationContext? authorizationContext;
            if (UploadCredentialToken.IsInReservedNamespace(bearerToken))
            {
                var uploadCredentialService = invocationContext.HttpContext.RequestServices
                                                               .GetRequiredService<UploadCredentialService>();
                authorizationContext = await uploadCredentialService.AuthenticateAsync(
                    bearerToken,
                    invocationContext.HttpContext.RequestAborted);
            }
            else
            {
                // AdminTokenService is registered only when admin operations are enabled; in an uploads-only
                // exposure it resolves to null and these routes fail closed, accepting scoped credentials exclusively.
                var adminTokenService = invocationContext.HttpContext.RequestServices.GetService<AdminTokenService>();
                authorizationContext = adminTokenService?.IsValidToken(bearerToken) == true
                    ? UploadCredentialAuthorizationContext.BootstrapAdmin
                    : null;
            }

            if (authorizationContext is null)
            {
                return Results.Unauthorized();
            }

            invocationContext.HttpContext.Items[AuthorizationContextKey] = authorizationContext;
            return await next(invocationContext);
        });

        return routeGroupBuilder;
    }
}
