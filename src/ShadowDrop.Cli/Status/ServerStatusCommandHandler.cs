// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Contracts;
using System.Net;
using System.Text.Json;

internal sealed class ServerStatusCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;

    public ServerStatusCommandHandler(CliConfigurationResolver configurationResolver,
                                      HttpClient httpClient,
                                      TextWriter standardOut,
                                      TextWriter standardError,
                                      TimeProvider timeProvider)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
        _timeProvider = timeProvider;
    }

    public async Task<Int32> ExecuteAsync(ServerStatusCommandOptions options, CancellationToken cancellationToken)
    {
        var mode = ResolveMode(options);
        if (options is { UploadAuthorized: true, Verbose: true })
        {
            return await FailAsync(options, mode, null, "mutually-exclusive-modes", "The --upload-authorized and --verbose options cannot be combined.");
        }

        ResolvedStatusConfiguration? configuration;
        try
        {
            configuration = ResolveConfiguration(options, mode);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return await FailAsync(options, mode, ServerStatusUrl.GetSafeDisplayValue(options.ServerUrlOverride), "configuration-invalid",
                                   "Configuration file invalid or unreadable.");
        }

        if (configuration is null)
        {
            return await FailAsync(options, mode, ServerStatusUrl.GetSafeDisplayValue(options.ServerUrlOverride), "configuration-invalid",
                                   ResolveConfigurationError(mode));
        }

        ServerStatusApiResponse response;
        try
        {
            response = await new ServerStatusApiClient(_httpClient, _timeProvider)
                .GetAsync(configuration.ServerUrl, mode, configuration.BearerToken, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await WriteFailureAsync(options, mode, configuration.ServerUrl.ToString(), ServerStatusOutcomes.Unreachable, "request-timeout", 3);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return await WriteFailureAsync(options, mode, configuration.ServerUrl.ToString(), ServerStatusOutcomes.Unreachable, "connection-failed", 3);
        }
        catch (JsonException)
        {
            return await WriteFailureAsync(options, mode, configuration.ServerUrl.ToString(), ServerStatusOutcomes.UnexpectedFailure,
                                           "invalid-response", 1, reachable: true);
        }

        return await HandleResponseAsync(options, mode, configuration.ServerUrl, response);
    }

    private static ServerStatusEvaluation Evaluate(Int32 protocolVersion, Boolean ready, HttpStatusCode statusCode)
    {
        var compatible = protocolVersion is >= OperationalStatusProtocol.MinimumSupportedVersion
                                            and <= OperationalStatusProtocol.MaximumSupportedVersion;
        if (!compatible)
        {
            return new(false, ServerStatusOutcomes.ProtocolIncompatible, null, 5);
        }

        if (!ready || statusCode == HttpStatusCode.ServiceUnavailable)
        {
            return new(true, ServerStatusOutcomes.NotReady, null, 2);
        }

        if (statusCode != HttpStatusCode.OK)
        {
            return new(true, ServerStatusOutcomes.UnexpectedFailure, "unexpected-response", 1);
        }

        return new(true, ServerStatusOutcomes.Healthy, null, 0);
    }

    private static String ResolveConfigurationError(ServerStatusMode mode) => mode switch
    {
        ServerStatusMode.Public => "Server URL invalid or missing.",
        ServerStatusMode.Upload => "Server URL or upload token invalid or missing.",
        ServerStatusMode.Admin => "Server URL or admin token invalid or missing.",
        _ => "Status configuration invalid."
    };

    private static ServerStatusMode ResolveMode(ServerStatusCommandOptions options) =>
        options.Verbose ? ServerStatusMode.Admin : options.UploadAuthorized ? ServerStatusMode.Upload : ServerStatusMode.Public;

    private async Task<Int32> FailAsync(
        ServerStatusCommandOptions options,
        ServerStatusMode mode,
        String? serverUrl,
        String error,
        String diagnostic)
    {
        await _standardError.WriteLineAsync(diagnostic);
        return await WriteFailureAsync(options, mode, serverUrl, ServerStatusOutcomes.UsageError, error, 1);
    }

    private async Task<Int32> HandleResponseAsync(
        ServerStatusCommandOptions options,
        ServerStatusMode mode,
        Uri serverUrl,
        ServerStatusApiResponse response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await WriteFailureAsync(options, mode, serverUrl.ToString(), ServerStatusOutcomes.Unauthorized, "authorization-failed", 4,
                                           reachable: true);
        }

        if (response.StatusCode == HttpStatusCode.NotFound && mode != ServerStatusMode.Public)
        {
            return await WriteFailureAsync(options, mode, serverUrl.ToString(), ServerStatusOutcomes.CapabilityDisabled,
                                           "capability-disabled", 1, reachable: true);
        }

        if (response.ProtocolVersion is { } protocolVersion
            && (protocolVersion < OperationalStatusProtocol.MinimumSupportedVersion
                || protocolVersion > OperationalStatusProtocol.MaximumSupportedVersion))
        {
            return await WriteFailureAsync(options, mode, serverUrl.ToString(), ServerStatusOutcomes.ProtocolIncompatible,
                                           null, 5, reachable: true, protocolCompatible: false);
        }

        if (response is { StatusCode: HttpStatusCode.ServiceUnavailable, Status: null } && mode == ServerStatusMode.Upload)
        {
            return await WriteFailureAsync(options, mode, serverUrl.ToString(), ServerStatusOutcomes.NotReady,
                                           "credential-provider-unavailable", 2, reachable: true);
        }

        if (response.Status is null)
        {
            return await WriteFailureAsync(options, mode, serverUrl.ToString(), ServerStatusOutcomes.UnexpectedFailure,
                                           "unexpected-response", 1, reachable: true);
        }

        return mode switch
        {
            ServerStatusMode.Public => await WriteStatusAsync(options, (PublicServerStatusContract)response.Status, serverUrl, response.StatusCode),
            ServerStatusMode.Upload => await WriteStatusAsync(options, (UploadServerStatusContract)response.Status, serverUrl, response.StatusCode),
            ServerStatusMode.Admin => await WriteStatusAsync(options, (AdminServerStatusContract)response.Status, serverUrl, response.StatusCode),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported server status mode.")
        };
    }

    private ResolvedStatusConfiguration? ResolveConfiguration(ServerStatusCommandOptions options, ServerStatusMode mode)
    {
        String? serverUrl;
        String? bearerToken = null;
        if (mode == ServerStatusMode.Admin)
        {
            var configuration = _configurationResolver.ResolveAdmin(options.ServerUrlOverride, options.AdminTokenOverride);
            serverUrl = configuration.ServerUrl;
            bearerToken = configuration.AdminToken;
        }
        else
        {
            var configuration = _configurationResolver.Resolve(options.ServerUrlOverride,
                                                               mode == ServerStatusMode.Upload ? options.UploadTokenOverride : null);
            serverUrl = configuration.ServerUrl;
            if (mode == ServerStatusMode.Upload)
            {
                bearerToken = configuration.UploadToken;
            }
        }

        if (!ServerStatusUrl.TryCreate(serverUrl, out var uri)
            || (mode != ServerStatusMode.Public && String.IsNullOrWhiteSpace(bearerToken)))
        {
            return null;
        }

        return new(uri, bearerToken);
    }

    private async Task<Int32> WriteFailureAsync(
        ServerStatusCommandOptions options,
        ServerStatusMode mode,
        String? serverUrl,
        String outcome,
        String? error,
        Int32 exitCode,
        Boolean reachable = false,
        Boolean? protocolCompatible = null)
    {
        await ServerStatusResultWriter.WriteAsync(new ServerStatusFailureCliResult(serverUrl, reachable, CliVersion.Current, protocolCompatible,
                                                                                   outcome, error, mode),
                                                  options.Json, _standardOut);
        return exitCode;
    }

    private async Task<Int32> WriteStatusAsync(
        ServerStatusCommandOptions options,
        PublicServerStatusContract status,
        Uri serverUrl,
        HttpStatusCode statusCode)
    {
        var evaluation = Evaluate(status.ProtocolVersion, status.Ready, statusCode);
        await ServerStatusResultWriter.WriteAsync(new PublicServerStatusCliResult(serverUrl.ToString(), true, CliVersion.Current,
                                                                                  evaluation.Compatible, evaluation.Outcome, evaluation.Error, status),
                                                  options.Json, _standardOut);
        return evaluation.ExitCode;
    }

    private async Task<Int32> WriteStatusAsync(
        ServerStatusCommandOptions options,
        UploadServerStatusContract status,
        Uri serverUrl,
        HttpStatusCode statusCode)
    {
        var evaluation = Evaluate(status.ProtocolVersion, status.Ready, statusCode);
        await ServerStatusResultWriter.WriteAsync(new UploadServerStatusCliResult(serverUrl.ToString(), true, CliVersion.Current,
                                                                                  evaluation.Compatible, evaluation.Outcome, evaluation.Error, status),
                                                  options.Json, _standardOut);
        return evaluation.ExitCode;
    }

    private async Task<Int32> WriteStatusAsync(
        ServerStatusCommandOptions options,
        AdminServerStatusContract status,
        Uri serverUrl,
        HttpStatusCode statusCode)
    {
        var evaluation = Evaluate(status.ProtocolVersion, status.Ready, statusCode);
        await ServerStatusResultWriter.WriteAsync(new AdminServerStatusCliResult(serverUrl.ToString(), true, CliVersion.Current,
                                                                                 evaluation.Compatible, evaluation.Outcome, evaluation.Error, status),
                                                  options.Json, _standardOut);
        return evaluation.ExitCode;
    }

    private sealed record ResolvedStatusConfiguration(Uri ServerUrl, String? BearerToken);

    private sealed record ServerStatusEvaluation(Boolean Compatible, String Outcome, String? Error, Int32 ExitCode);
}
