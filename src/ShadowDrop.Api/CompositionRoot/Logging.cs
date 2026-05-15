// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.CompositionRoot;

using Serilog;

public static class Logging
{
    public static IHostApplicationBuilder ConfigureDefaultLogging(this IHostApplicationBuilder builder)
    {
        builder.Services
               .AddSerilog((serviceProvider, loggerConfiguration) =>
                               loggerConfiguration.ReadFrom.Configuration(builder.Configuration)
                                                  .ReadFrom.Services(serviceProvider)
                                                  .Enrich.FromLogContext()
                                                  .WriteTo.Console(),
                           false,
                           false);
        return builder;
    }

    public static ILogger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();
}
