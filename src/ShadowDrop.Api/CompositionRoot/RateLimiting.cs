// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using System.Threading.RateLimiting;

public static class RateLimiting
{
    public const String UploadRateLimitPolicyName = "admin-upload-fixed-window";

    public static WebApplicationBuilder ConfigureRateLimiter(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (!context.HttpContext.Response.HasStarted)
                {
                    context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        Error = "Too many requests."
                    }, cancellationToken);
                }
            };

            options.AddPolicy(UploadRateLimitPolicyName, httpContext =>
            {
                var clientAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    clientAddress,
                    _ => new()
                    {
                        PermitLimit = 3,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true
                    });
            });
        });

        return builder;
    }
}
