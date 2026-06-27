// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Tls;

internal sealed record CliApplicationServices(
    CliConfigurationResolver ConfigurationResolver,
    Func<CliTlsOptions, HttpClient> HttpClientFactory,
    Stream StandardOutStream,
    TextWriter StandardOut,
    TextWriter StandardError,
    ICliInteractiveSession InteractiveSession,
    TimeProvider TimeProvider)
{
    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError)
        : this(configurationResolver,
               _ => httpClient,
               Stream.Null,
               standardOut,
               standardError,
               new SpectreCliInteractiveSession(standardError),
               TimeProvider.System) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  Stream standardOutStream,
                                  TextWriter standardOut,
                                  TextWriter standardError)
        : this(configurationResolver,
               _ => httpClient,
               standardOutStream,
               standardOut,
               standardError,
               new SpectreCliInteractiveSession(standardError),
               TimeProvider.System) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  Stream standardOutStream,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider)
        : this(configurationResolver,
               _ => httpClient,
               standardOutStream,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider) { }

    public static CliApplicationServices CreateDefault() =>
        new(new(new(), new EnvironmentReader()),
            CliHttpClientFactory.CreateClient,
            Console.OpenStandardOutput(),
            Console.Out,
            Console.Error,
            new SpectreCliInteractiveSession(Console.Error),
            TimeProvider.System);
}
