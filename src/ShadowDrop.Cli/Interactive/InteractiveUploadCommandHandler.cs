// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Cli.Uploads;
using System.Text.Json;

internal sealed class InteractiveUploadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    ICliInteractiveSession interactiveSession,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider)
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
        catch (IOException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }
        catch (JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }

        var serverUrl = ResolveServerUrl(configuration.ServerUrl);
        var uploadToken = ResolveUploadToken(configuration.UploadToken);
        var files = ResolveFiles(options.Files);

        interactiveSession.ShowSummary("Upload plan",
                                       files.Select(file => ("File", file.FullName))
                                            .Concat([("Server", serverUrl.AbsoluteUri)])
                                            .ToArray());

        var uploader = new UploadCommandExecutor(httpClient);
        var uploadResult = await uploader.ExecuteAsync(files, serverUrl, uploadToken, cancellationToken);
        for (var index = 0; index < uploadResult.Files.Count; index++)
        {
            var fileResult = uploadResult.Files[index];
            if (fileResult.UploadedFileId is not null)
            {
                interactiveSession.ShowMessage($"Uploaded file {index + 1} of {uploadResult.Files.Count}.");
            }
            else
            {
                await standardError.WriteLineAsync($"File {index + 1} failed: {fileResult.ErrorMessage}");
            }
        }

        if (!uploadResult.AllSucceeded || String.IsNullOrWhiteSpace(uploadResult.ShareSecretHex))
        {
            return 1;
        }

        var shareRequest = BuildShareRequest(uploadResult, PromptShareOptions());
        CreateShareCliResult shareResult;
        try
        {
            shareResult = await new CreateShareApiClient(httpClient).CreateAsync(serverUrl, uploadToken, shareRequest, cancellationToken);
        }
        catch (CreateShareCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        var shareLink = ShareDownloadUriFactory.CreateManifestUri(serverUrl, shareResult.ShareToken).AbsoluteUri;
        interactiveSession.ShowSummary("Share created",
        [
            ("Share link", shareLink),
            ("Delivery mode", shareRequest.DirectHttpEnabled ? "Direct HTTP" : "Separate key"),
            ("Download bearer token", shareRequest.GenerateDownloadBearerToken ? "Required" : "Not required"),
            ("Share key", options.OutputSecret ? "Written to stdout." : "Hidden unless you opt in.")
        ]);

        var shouldOutputSecrets = options.OutputSecret
                                  || interactiveSession.PromptConfirmation("Display the share key and any generated download bearer token now?", false);
        if (!shouldOutputSecrets)
        {
            return 0;
        }

        await standardOut.WriteLineAsync($"secret:{uploadResult.ShareSecretHex}");
        if (!String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
        {
            await standardOut.WriteLineAsync($"download-bearer-token:{shareResult.DownloadBearerToken}");
        }

        return 0;
    }

    private static CreateShareCliRequest BuildShareRequest(UploadExecutionResult uploadResult, SharePromptResult promptResult) =>
        new(promptResult.ExpiresAtUtc,
            uploadResult.Files.Select(static result => new CreateShareCliFileRequest(result.UploadedFileId!.Value, result.File.Name))
                        .ToArray(),
            promptResult.DirectHttpEnabled,
            promptResult.RequireDownloadBearerToken,
            promptResult.RequireDownloadBearerToken ? promptResult.ExpiresAtUtc : null);

    private SharePromptResult PromptShareOptions()
    {
        var choices = new ExpirationChoice[]
        {
            new("1 hour", TimeSpan.FromHours(1)),
            new("1 day", TimeSpan.FromDays(1)),
            new("7 days", TimeSpan.FromDays(7)),
            new("30 days", TimeSpan.FromDays(30))
        };
        var expirationChoice = interactiveSession.PromptSelection("Select the share expiration:", choices, static choice => choice.Label);
        var directHttpEnabled = interactiveSession.PromptConfirmation("Enable direct HTTP downloads?", false);
        var requireDownloadBearerToken = !directHttpEnabled
                                         && interactiveSession.PromptConfirmation("Require a download bearer token?", false);
        return new(timeProvider.GetUtcNow().Add(expirationChoice.Duration), directHttpEnabled, requireDownloadBearerToken);
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
            {
                if (String.IsNullOrWhiteSpace(value))
                {
                    return "Enter a local file path.";
                }

                return null;
            });
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

    private sealed record ExpirationChoice(String Label, TimeSpan Duration);

    private sealed record SharePromptResult(DateTimeOffset ExpiresAtUtc, Boolean DirectHttpEnabled, Boolean RequireDownloadBearerToken);
}
