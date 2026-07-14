// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new
           {
               Status = "ok"
           }))
           .WithName("Liveness");

        app.MapGet("/health/ready", CheckReadinessAsync)
           .WithName("Readiness");

        return app;
    }

    private static async Task<IResult> CheckReadinessAsync(IReadinessCheck readinessCheck, CancellationToken cancellationToken) =>
        await readinessCheck.IsReadyAsync(cancellationToken)
            ? Results.Ok(new
            {
                Status = "ok"
            })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}
