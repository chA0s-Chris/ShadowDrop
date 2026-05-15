// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
           {
               Status = "ok"
           }))
           .WithName("Health");

        return app;
    }
}
