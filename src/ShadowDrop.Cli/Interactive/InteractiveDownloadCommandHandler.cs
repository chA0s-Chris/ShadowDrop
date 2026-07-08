// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class InteractiveDownloadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    ICliInteractiveSession interactiveSession,
    TextWriter standardError,
    CliBannerWriter bannerWriter)
{
    public async Task<Int32> ExecuteAsync(DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!interactiveSession.IsInteractiveSupported)
        {
            await standardError.WriteLineAsync(InteractiveModeMessages.TerminalRequired);
            return 1;
        }

        if (options.QueuePath is not null)
        {
            await standardError.WriteLineAsync("The --interactive option cannot be combined with --queue.");
            return 1;
        }

        var handler = new DownloadCommandHandler(configurationResolver, httpClient, standardError, NullDownloadProgressReporter.Instance, bannerWriter);
        Byte[]? shareKeyBytes = null;
        try
        {
            shareKeyBytes = await ResolveShareKeyBytesAsync(handler, options, cancellationToken);
            if (shareKeyBytes is null)
            {
                await standardError.WriteLineAsync("Share key invalid or missing.");
                return 1;
            }

            var shareReference = ResolveShareReference(options.ShareToken, options.ServerUrlOverride);
            var resolvedManifest = await ResolveManifestAsync(handler, shareReference, options.BearerToken, cancellationToken);
            var selectedFiles = SelectFiles(resolvedManifest.Manifest, options.FileId);
            var outputPaths = ResolveOutputPaths(selectedFiles);

            for (var index = 0; index < selectedFiles.Count; index++)
            {
                await handler.DownloadToFileAsync(resolvedManifest.ServerUrl,
                                                  resolvedManifest.ShareToken,
                                                  selectedFiles[index],
                                                  shareKeyBytes,
                                                  resolvedManifest.BearerToken,
                                                  outputPaths[index],
                                                  cancellationToken);
            }

            // The interactive summary is the first real output once every file has downloaded successfully;
            // this handler has no standardOut writer of its own, so the banner goes to stderr like its errors.
            await bannerWriter.WriteToStandardErrorAsync(standardError, cancellationToken);
            interactiveSession.ShowSummary("Download complete",
                                           selectedFiles.Select((file, index) => ($"{file.FileName}", outputPaths[index]))
                                                        .ToArray());
            return 0;
        }
        catch (DownloadCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (IOException)
        {
            await standardError.WriteLineAsync("Download failed due to a local I/O error.");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync("Download failed due to a local I/O error.");
            return 1;
        }
        finally
        {
            if (shareKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(shareKeyBytes);
            }
        }
    }

    // Routed through the shared destination resolver so a server-announced name cannot introduce path segments
    // into the prompted default, exactly as in the non-interactive single-file download.
    private static String ResolveOutputFileName(ShareManifestFileContract file) => DownloadDestinationResolver.ResolveAnnouncedFileName(file);

    private CliResolvedConfiguration ResolveConfiguration(String? serverUrlOverride)
    {
        try
        {
            return configurationResolver.Resolve(serverUrlOverride, null);
        }
        catch (IOException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
        catch (JsonException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
    }

    private async Task<ResolvedManifest> ResolveManifestAsync(DownloadCommandHandler handler,
                                                              InteractiveShareReference shareReference,
                                                              String? bearerToken,
                                                              CancellationToken cancellationToken)
    {
        var currentBearerToken = bearerToken;
        while (true)
        {
            try
            {
                var manifest = await handler.GetManifestAsync(shareReference.ServerUrl, shareReference.ShareToken, currentBearerToken, cancellationToken);
                return new(shareReference.ServerUrl, shareReference.ShareToken, manifest, currentBearerToken);
            }
            catch (DownloadCommandException exception) when (String.Equals(exception.Message, "Download authorization failed.", StringComparison.Ordinal)
                                                             && String.IsNullOrWhiteSpace(currentBearerToken))
            {
                currentBearerToken = interactiveSession.PromptText("Download bearer token:", secret: true, validate: static value =>
                                                                       String.IsNullOrWhiteSpace(value) ? "Enter the bearer token." : null);
            }
        }
    }

    private IReadOnlyList<String> ResolveOutputPaths(IReadOnlyList<ShareManifestFileContract> files)
    {
        if (files.Count == 1)
        {
            var outputFileName = ResolveOutputFileName(files[0]);
            var outputPath = interactiveSession.PromptText("Output file path:", Path.Combine(Environment.CurrentDirectory, outputFileName),
                                                           validate: static value => String.IsNullOrWhiteSpace(value)
                                                               ? "Enter an output path."
                                                               : null);
            return [outputPath];
        }

        var outputDirectory = interactiveSession.PromptText("Output directory:", Environment.CurrentDirectory, validate: static value =>
                                                                String.IsNullOrWhiteSpace(value) ? "Enter an output directory." : null);
        return files.Select(file => Path.Combine(outputDirectory, ResolveOutputFileName(file))).ToArray();
    }

    private async Task<Byte[]?> ResolveShareKeyBytesAsync(DownloadCommandHandler handler, DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        var shareKeyBytes = await handler.ResolveShareKeyBytesAsync(options, cancellationToken);
        if (shareKeyBytes is not null)
        {
            return shareKeyBytes;
        }

        var shareKey = interactiveSession.PromptText("Share key:", secret: true, validate: static value =>
                                                         String.IsNullOrWhiteSpace(value) ? "Enter the share key." : null);
        return DownloadCommandHandler.DecodeShareKey(shareKey);
    }

    private InteractiveShareReference ResolveShareReference(String? shareToken, String? serverUrlOverride)
    {
        var enteredShareToken = String.IsNullOrWhiteSpace(shareToken)
            ? interactiveSession.PromptText("Share URL or share token:", validate: static value =>
                                                String.IsNullOrWhiteSpace(value) ? "Enter a share URL or share token." : null)
            : shareToken;

        // Reuse the same token-or-URL parsing as the non-interactive download and queue commands.
        if (!ShareReferenceResolver.TryResolve(enteredShareToken, null, out var resolvedServerUrl, out var resolvedToken))
        {
            throw new DownloadCommandException("Share token invalid or missing.");
        }

        if (resolvedServerUrl is not null)
        {
            return new(resolvedServerUrl, resolvedToken);
        }

        var configuration = ResolveConfiguration(serverUrlOverride);
        if (Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var configuredServerUrl)
            && (configuredServerUrl.Scheme == Uri.UriSchemeHttp || configuredServerUrl.Scheme == Uri.UriSchemeHttps))
        {
            return new(configuredServerUrl, resolvedToken);
        }

        var configuredServerUrlValue = configuration.ServerUrl;
        while (true)
        {
            var candidate = interactiveSession.PromptText("ShadowDrop server URL:", configuredServerUrlValue, validate: static value =>
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
                return new(serverUrl, resolvedToken);
            }

            configuredServerUrlValue = null;
        }
    }

    private IReadOnlyList<ShareManifestFileContract> SelectFiles(ShareManifestContract manifest, String? fileId)
    {
        if (!String.IsNullOrWhiteSpace(fileId))
        {
            return [DownloadCommandHandler.SelectFileById(manifest.Files!, DownloadCommandHandler.ParseFileId(fileId))];
        }

        while (true)
        {
            var selectedFiles = interactiveSession.PromptMultiSelection("Select files to download:", manifest.Files!, static file =>
                                                                            $"{file.FileName} ({file.Length} bytes)");
            if (selectedFiles.Count > 0)
            {
                return selectedFiles;
            }

            interactiveSession.ShowError("Select at least one file.");
        }
    }

    private sealed record InteractiveShareReference(Uri ServerUrl, String ShareToken);

    private sealed record ResolvedManifest(Uri ServerUrl, String ShareToken, ShareManifestContract Manifest, String? BearerToken);
}
