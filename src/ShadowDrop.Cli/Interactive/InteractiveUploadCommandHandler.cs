// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Uploads;
using System.Text.Json;

/// <summary>
/// Guided upload: collects the server, token, files, and share options interactively, then delegates the
/// actual upload, share creation, and credential delivery to the shared <see cref="UploadCommandHandler"/>
/// so the orchestration and result format match the non-interactive command exactly.
/// </summary>
internal sealed class InteractiveUploadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    ICliInteractiveSession interactiveSession,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider,
    CliBannerWriter bannerWriter)
{
    public async Task<Int32> ExecuteAsync(UploadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!interactiveSession.IsInteractiveSupported)
        {
            await standardError.WriteLineAsync(InteractiveModeMessages.TerminalRequired);
            return 1;
        }

        CliResolvedConfiguration configuration;
        try
        {
            configuration = configurationResolver.Resolve(options.ServerUrlOverride, options.UploadTokenOverride);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }

        var serverUrl = ResolveServerUrl(configuration.ServerUrl);
        var uploadToken = ResolveUploadToken(configuration.UploadToken);
        var files = ResolveFiles(options.Files);
        var shareChoices = PromptShareOptions();

        interactiveSession.ShowSummary("Upload plan",
                                       files.Select(file => ("File", file.FullName))
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
                                                     options.DisplayNameMappings);

        return await new UploadCommandHandler(configurationResolver, httpClient, standardOut, standardError, timeProvider, bannerWriter)
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
        var expirationChoice = interactiveSession.PromptSelection("Select the share expiration:", choices, static choice => choice.Label);
        var directHttp = interactiveSession.PromptConfirmation("Enable direct HTTP downloads?", false);
        var generateDownloadToken = !directHttp && interactiveSession.PromptConfirmation("Require a download bearer token?", false);
        return new(expirationChoice.ExpiresIn, expirationChoice.Label, directHttp, generateDownloadToken);
    }

    private IReadOnlyList<FileInfo> ResolveFiles(IReadOnlyList<FileInfo> files)
    {
        if (files.Count > 0)
        {
            return files;
        }

        List<FileInfo> selectedFiles = [];
        do
        {
            var path = interactiveSession.PromptText("Path to a file to upload:", validate: static value =>
                                                         String.IsNullOrWhiteSpace(value) ? "Enter a local file path." : null);
            selectedFiles.Add(new FileInfo(path));
        } while (interactiveSession.PromptConfirmation("Add another file?", false));

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
            var candidate = interactiveSession.PromptText("ShadowDrop server URL:", configuredServerUrl, validate: static value =>
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

        return interactiveSession.PromptText("Upload authorization token:", secret: true, validate: static value =>
                                                 String.IsNullOrWhiteSpace(value) ? "Enter an upload token." : null);
    }

    private sealed record ExpirationChoice(String Label, String ExpiresIn);

    private sealed record SharePromptResult(String ExpiresIn, String ExpirationLabel, Boolean DirectHttp, Boolean GenerateDownloadToken);
}
