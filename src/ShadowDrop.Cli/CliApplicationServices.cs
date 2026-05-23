// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Configuration;

internal sealed record CliApplicationServices(
    CliConfigurationResolver ConfigurationResolver,
    HttpClient HttpClient,
    TextWriter StandardOut,
    TextWriter StandardError)
{
    public static CliApplicationServices CreateDefault() =>
        new(new(new(), new EnvironmentReader()),
            new(),
            Console.Out,
            Console.Error);
}
