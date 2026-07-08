// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Terminals;
using ShadowDrop.Cli.Tls;

internal sealed record CliApplicationServices(
    CliConfigurationResolver ConfigurationResolver,
    Func<CliTlsOptions, HttpClient> HttpClientFactory,
    TextWriter StandardOut,
    TextWriter StandardError,
    ICliInteractiveSession InteractiveSession,
    TimeProvider TimeProvider,
    IDownloadProgressReporterFactory DownloadProgressReporterFactory,
    ITerminalCapabilityProvider TerminalCapabilityProvider)
{
    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError)
        : this(configurationResolver,
               _ => httpClient,
               standardOut,
               standardError,
               new SpectreCliInteractiveSession(standardError),
               TimeProvider.System,
               new DownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
               new TerminalCapabilityProvider()) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider)
        : this(configurationResolver,
               _ => httpClient,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider,
               new DownloadProgressReporterFactory(standardOut, standardError, timeProvider),
               new TerminalCapabilityProvider()) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider,
                                  IDownloadProgressReporterFactory downloadProgressReporterFactory)
        : this(configurationResolver,
               _ => httpClient,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider,
               downloadProgressReporterFactory,
               new TerminalCapabilityProvider()) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider,
                                  IDownloadProgressReporterFactory downloadProgressReporterFactory,
                                  ITerminalCapabilityProvider terminalCapabilityProvider)
        : this(configurationResolver,
               _ => httpClient,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider,
               downloadProgressReporterFactory,
               terminalCapabilityProvider) { }

    public static CliApplicationServices CreateDefault()
    {
        // A single provider is shared so banner rendering and download progress selection use the same
        // detection implementation rather than two independently constructed providers. The provider re-reads
        // the environment on each call, so this is a single source of truth, not caching.
        var terminalCapabilityProvider = new TerminalCapabilityProvider();
        return new(new(new(), new EnvironmentReader()),
                   CliHttpClientFactory.CreateClient,
                   Console.Out,
                   Console.Error,
                   new SpectreCliInteractiveSession(Console.Error),
                   TimeProvider.System,
                   new DownloadProgressReporterFactory(Console.Out, Console.Error, TimeProvider.System, terminalCapabilityProvider),
                   terminalCapabilityProvider);
    }
}
