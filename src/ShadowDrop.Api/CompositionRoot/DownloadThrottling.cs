// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
#if ENABLE_THROTTLE_DOWNLOAD
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;
using ShadowDrop.Api.Downloads;
using System.Globalization;

/// <summary>
/// DEVELOPMENT-ONLY middleware that paces response bodies to a configurable byte rate so streamed downloads are slow
/// enough to observe the CLI's live progress output.
/// </summary>
/// <remarks>
/// Compiled only when the <c>ENABLE_THROTTLE_DOWNLOAD</c> symbol is defined (Debug builds). Even then it stays inert unless
/// <c>SHADOWDROP_DEV_THROTTLE_BPS</c> is set to a positive byte rate, so normal Debug runs are unaffected.
/// </remarks>
internal static class DownloadThrottling
{
    private const String ThrottleEnvironmentVariable = "SHADOWDROP_DEV_THROTTLE_BPS";

    public static WebApplication UseDevelopmentDownloadThrottle(this WebApplication app, ILogger logger)
    {
        var configuredRate = Environment.GetEnvironmentVariable(ThrottleEnvironmentVariable);
        if (!Int64.TryParse(configuredRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytesPerSecond) || bytesPerSecond <= 0)
        {
            return app;
        }

        logger.Warning("DEVELOPMENT-ONLY download throttle is active at {BytesPerSecond} B/s ({EnvironmentVariable}). Never enable this in production.",
                       bytesPerSecond,
                       ThrottleEnvironmentVariable);

        app.Use(async (context, next) =>
        {
            var originalBody = context.Response.Body;
            context.Response.Body = new ThrottledStream(originalBody, bytesPerSecond);
            try
            {
                await next();
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        });

        return app;
    }
}
#endif
