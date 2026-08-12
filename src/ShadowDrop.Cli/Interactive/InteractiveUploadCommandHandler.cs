// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Cli.Uploads.Progress;
using System.Text.Json;

/// <summary>
/// Guided upload: collects the server, token, files, and share options interactively, then delegates the
/// actual upload, share creation, and credential delivery to the shared <see cref="UploadCommandHandler"/>
/// so the orchestration and result format match the non-interactive command exactly.
/// </summary>
internal sealed class InteractiveUploadCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly ICliInteractiveSession _interactiveSession;
    private readonly TextWriter _standardError;
    private readonly TextReader _standardInput;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadProgressReporterFactory _uploadProgressReporterFactory;

    public InteractiveUploadCommandHandler(CliConfigurationResolver configurationResolver,
                                           HttpClient httpClient,
                                           ICliInteractiveSession interactiveSession,
                                           TextWriter standardOut,
                                           TextWriter standardError,
                                           TimeProvider timeProvider,
                                           IUploadProgressReporterFactory uploadProgressReporterFactory,
                                           TextReader? standardInput = null)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _interactiveSession = interactiveSession;
        _standardOut = standardOut;
        _standardError = standardError;
        _standardInput = standardInput ?? Console.In;
        _timeProvider = timeProvider;
        _uploadProgressReporterFactory = uploadProgressReporterFactory;
    }

    public async Task<Int32> ExecuteAsync(UploadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_interactiveSession.IsInteractiveSupported)
        {
            await _standardError.WriteLineAsync(InteractiveModeMessages.TerminalRequired);
            return 1;
        }

        if (!UploadCommandOptionsValidator.TryValidateLocalOptionCombinations(options, out var optionError))
        {
            await _standardError.WriteLineAsync(optionError);
            return 1;
        }

        CliResolvedConfiguration configuration;
        try
        {
            configuration = _configurationResolver.Resolve(options.ServerUrlOverride, options.UploadTokenOverride);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await _standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }

        var serverUrl = ResolveServerUrl(configuration.ServerUrl);
        var uploadToken = ResolveUploadToken(configuration.UploadToken);
        var files = ResolveFiles(options);
        var shareChoices = PromptShareOptions();

        var suppliedPaths = options.InputPaths ?? [];
        _interactiveSession.ShowSummary("Upload plan",
                                        files.Select(file => ("File", file.FullName))
                                             .Concat(suppliedPaths.Select(static path => ("Input", path)))
                                             .Concat((options.FilesFrom ?? []).Select(static source => ("Input list", source)))
                                             .Concat(
                                             [
                                                 ("Server", serverUrl.AbsoluteUri),
                                                 ("Expiration", shareChoices.ExpirationLabel),
                                                 ("Delivery mode", shareChoices.DirectHttp ? "Direct HTTP" : "Separate key"),
                                                 ("Download bearer token", shareChoices.GenerateDownloadToken ? "Required" : "Not required")
                                             ])
                                             .ToArray());

        // Delegate to the shared end-to-end handler so the upload, share creation, and credential delivery
        // (share URL + share key + any bearer token on stdout) behave identically to the non-interactive command.
        var uploadOptions = new UploadCommandOptions(files.ToArray(),
                                                     serverUrl.AbsoluteUri,
                                                     uploadToken,
                                                     shareChoices.ExpiresIn,
                                                     shareChoices.DirectHttp,
                                                     shareChoices.GenerateDownloadToken,
                                                     options.SecretsOut,
                                                     options.QueueOut,
                                                     options.EmbedSecrets,
                                                     options.Json,
                                                     options.Force,
                                                     options.DisplayName,
                                                     options.DisplayNameMappings,
                                                     // Carried through so an interactive upload validates and generates
                                                     // exactly the same queue destinations as the equivalent command line.
                                                     options.InputRoot,
                                                     options.Flatten,
                                                     options.WorkingDirectory,
                                                     options.InputPaths,
                                                     options.Recursive,
                                                     options.IncludePatterns,
                                                     options.ExcludePatterns,
                                                     options.FilesFrom);

        return await new UploadCommandHandler(_configurationResolver,
                                              _httpClient,
                                              _standardOut,
                                              _standardError,
                                              _timeProvider,
                                              _uploadProgressReporterFactory,
                                              _standardInput)
            .ExecuteAsync(uploadOptions, cancellationToken);
    }

    private SharePromptResult PromptShareOptions()
    {
        var choices = new ExpirationChoice[]
        {
            new("1 hour", "1h"),
            new("1 day", "1d"),
            new("7 days", "7d"),
            new("30 days", "30d")
        };
        var expirationChoice = _interactiveSession.PromptSelection("Select the share expiration:", choices, static choice => choice.Label);
        var directHttp = _interactiveSession.PromptConfirmation("Enable direct HTTP downloads?", false);
        var generateDownloadToken = !directHttp && _interactiveSession.PromptConfirmation("Require a download bearer token?", false);
        return new(expirationChoice.ExpiresIn, expirationChoice.Label, directHttp, generateDownloadToken);
    }

    private IReadOnlyList<FileInfo> ResolveFiles(UploadCommandOptions options)
    {
        if (options.Files.Length > 0 || options.InputPaths is { Length: > 0 } || options.FilesFrom is { Length: > 0 })
        {
            return options.Files;
        }

        List<FileInfo> selectedFiles = [];
        do
        {
            var path = _interactiveSession.PromptText("Path to a file to upload:", validate: static value =>
                                                          String.IsNullOrWhiteSpace(value) ? "Enter a local file path." : null);
            selectedFiles.Add(new(path));
        } while (_interactiveSession.PromptConfirmation("Add another file?", false));

        return selectedFiles;
    }

    private Uri ResolveServerUrl(String? configuredServerUrl)
    {
        if (Uri.TryCreate(configuredServerUrl, UriKind.Absolute, out var configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
        {
            return configuredUri;
        }

        while (true)
        {
            var candidate = _interactiveSession.PromptText("ShadowDrop server URL:", configuredServerUrl, validate: static value =>
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    return null;
                }

                return "Enter a valid http:// or https:// URL.";
            });

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var serverUrl)
                && (serverUrl.Scheme == Uri.UriSchemeHttp || serverUrl.Scheme == Uri.UriSchemeHttps))
            {
                return serverUrl;
            }

            configuredServerUrl = null;
        }
    }

    private String ResolveUploadToken(String? configuredUploadToken)
    {
        if (!String.IsNullOrWhiteSpace(configuredUploadToken))
        {
            return configuredUploadToken;
        }

        return _interactiveSession.PromptText("Upload authorization token:", secret: true, validate: static value =>
                                                  String.IsNullOrWhiteSpace(value) ? "Enter an upload token." : null);
    }

    private sealed record ExpirationChoice(String Label, String ExpiresIn);

    private sealed record SharePromptResult(String ExpiresIn, String ExpirationLabel, Boolean DirectHttp, Boolean GenerateDownloadToken);
}
