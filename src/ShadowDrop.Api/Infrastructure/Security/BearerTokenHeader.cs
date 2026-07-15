// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using Microsoft.Extensions.Primitives;

internal static class BearerTokenHeader
{
    private const String Prefix = "Bearer ";

    public static Boolean TryRead(StringValues authorizationHeader, out String bearerToken)
    {
        var headerValue = authorizationHeader.ToString();
        if (!headerValue.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            bearerToken = String.Empty;
            return false;
        }

        bearerToken = headerValue[Prefix.Length..].Trim();
        return !String.IsNullOrWhiteSpace(bearerToken);
    }
}
