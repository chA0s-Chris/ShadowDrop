// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Configuration;

internal sealed record CliApplicationServices(
    CliConfigurationResolver ConfigurationResolver,
    HttpClient HttpClient,
    Stream StandardOutStream,
    TextWriter StandardOut,
    TextWriter StandardError)
{
    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError)
        : this(configurationResolver, httpClient, Stream.Null, standardOut, standardError) { }

    public static CliApplicationServices CreateDefault() =>
        new(new(new(), new EnvironmentReader()),
            new(),
            Console.OpenStandardOutput(),
            Console.Out,
            Console.Error);
}
