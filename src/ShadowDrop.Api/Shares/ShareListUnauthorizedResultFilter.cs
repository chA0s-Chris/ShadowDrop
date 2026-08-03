// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Contracts;

internal sealed class ShareListUnauthorizedResultFilter : IEndpointFilter
{
    public async ValueTask<Object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        return result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status401Unauthorized }
            ? Results.Json(new OperationalErrorContract(OperationalErrorReasons.Unauthorized),
                           OperationalStatusJsonSerializerContext.Default.OperationalErrorContract,
                           statusCode: StatusCodes.Status401Unauthorized)
            : result;
    }
}
