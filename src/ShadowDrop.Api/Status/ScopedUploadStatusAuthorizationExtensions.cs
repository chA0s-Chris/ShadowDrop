// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

internal static class ScopedUploadStatusAuthorizationExtensions
{
    public static RouteGroupBuilder RequireScopedUploadStatusBearerToken(this RouteGroupBuilder routes)
    {
        routes.AddEndpointFilter<ScopedUploadStatusAuthorizationFilter>();
        return routes;
    }
}
