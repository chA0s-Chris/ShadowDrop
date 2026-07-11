// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Terminals;
using ShadowDrop.Cli.Tls;
using ShadowDrop.Cli.Uploads.Progress;

internal sealed record CliApplicationServices(
    CliConfigurationResolver ConfigurationResolver,
    Func<CliTlsOptions, HttpClient> HttpClientFactory,
    TextWriter StandardOut,
    TextWriter StandardError,
    ICliInteractiveSession InteractiveSession,
    TimeProvider TimeProvider,
    IDownloadProgressReporterFactory DownloadProgressReporterFactory,
    IUploadProgressReporterFactory UploadProgressReporterFactory,
    ITerminalCapabilityProvider TerminalCapabilityProvider)
{
    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError)
        : this(configurationResolver,
               httpClient,
               standardOut,
               standardError,
               new SpectreCliInteractiveSession(standardError),
               TimeProvider.System) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider)
        : this(configurationResolver,
               httpClient,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider,
               new DownloadProgressReporterFactory(standardOut, standardError, timeProvider)) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  HttpClient httpClient,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider,
                                  IDownloadProgressReporterFactory downloadProgressReporterFactory)
        : this(configurationResolver,
               httpClient,
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
               new UploadProgressReporterFactory(standardError, timeProvider, terminalCapabilityProvider),
               terminalCapabilityProvider) { }

    public CliApplicationServices(CliConfigurationResolver configurationResolver,
                                  Func<CliTlsOptions, HttpClient> httpClientFactory,
                                  TextWriter standardOut,
                                  TextWriter standardError,
                                  ICliInteractiveSession interactiveSession,
                                  TimeProvider timeProvider,
                                  IDownloadProgressReporterFactory downloadProgressReporterFactory,
                                  ITerminalCapabilityProvider terminalCapabilityProvider)
        : this(configurationResolver,
               httpClientFactory,
               standardOut,
               standardError,
               interactiveSession,
               timeProvider,
               downloadProgressReporterFactory,
               new UploadProgressReporterFactory(standardError, timeProvider, terminalCapabilityProvider),
               terminalCapabilityProvider) { }

    public static CliApplicationServices CreateDefault()
    {
        // A single provider is shared so startup-banner rendering and download progress selection use the same
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
                   new UploadProgressReporterFactory(Console.Error, TimeProvider.System, terminalCapabilityProvider),
                   terminalCapabilityProvider);
    }
}
