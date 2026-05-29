// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class InteractiveDownloadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    ICliInteractiveSession interactiveSession,
    TextWriter standardError)
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

        var handler = new DownloadCommandHandler(configurationResolver, httpClient, Stream.Null, standardError);
        Byte[]? shareKeyBytes = null;
        try
        {
            shareKeyBytes = await ResolveShareKeyBytesAsync(handler, options, cancellationToken);
            if (shareKeyBytes is null)
            {
                await standardError.WriteLineAsync("Share key invalid or missing.");
                return 1;
            }

            var shareReference = ResolveShareReference(options.ShareId, options.ServerUrlOverride);
            var resolvedManifest = await ResolveManifestAsync(handler, shareReference, options.BearerToken, cancellationToken);
            var selectedFiles = SelectFiles(resolvedManifest.Manifest, options.FileId);
            var outputPaths = ResolveOutputPaths(selectedFiles);

            for (var index = 0; index < selectedFiles.Count; index++)
            {
                await handler.DownloadToFileAsync(resolvedManifest.ServerUrl,
                                                  resolvedManifest.ShareId,
                                                  selectedFiles[index],
                                                  shareKeyBytes,
                                                  resolvedManifest.BearerToken,
                                                  outputPaths[index],
                                                  cancellationToken);
            }

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

    private static String ResolveOutputFileName(ShareManifestFileContract file)
    {
        var fileName = Path.GetFileName(file.FileName ?? String.Empty);
        return String.IsNullOrWhiteSpace(fileName)
            ? file.FileId ?? "download.bin"
            : fileName;
    }

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
                var manifest = await handler.GetManifestAsync(shareReference.ServerUrl, shareReference.ShareId, currentBearerToken, cancellationToken);
                return new(shareReference.ServerUrl, shareReference.ShareId, manifest, currentBearerToken);
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

    private InteractiveShareReference ResolveShareReference(String? shareId, String? serverUrlOverride)
    {
        var enteredShareId = String.IsNullOrWhiteSpace(shareId)
            ? interactiveSession.PromptText("Share URL or share token:", validate: static value =>
                                                String.IsNullOrWhiteSpace(value) ? "Enter a share URL or share token." : null)
            : shareId;

        if (Uri.TryCreate(enteredShareId, UriKind.Absolute, out var absoluteShareUri)
            && (absoluteShareUri.Scheme == Uri.UriSchemeHttp || absoluteShareUri.Scheme == Uri.UriSchemeHttps))
        {
            var segments = absoluteShareUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && String.Equals(segments[^2], "d", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = segments.Length == 2 ? "/" : $"/{String.Join('/', segments[..^2])}/";
                var builder = new UriBuilder(absoluteShareUri)
                {
                    Path = basePath,
                    Query = String.Empty,
                    Fragment = String.Empty
                };
                return new(builder.Uri, Uri.UnescapeDataString(segments[^1]));
            }

            throw new DownloadCommandException("Share id invalid or missing.");
        }

        var configuration = ResolveConfiguration(serverUrlOverride);
        if (Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var configuredServerUrl)
            && (configuredServerUrl.Scheme == Uri.UriSchemeHttp || configuredServerUrl.Scheme == Uri.UriSchemeHttps))
        {
            return new(configuredServerUrl, enteredShareId);
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
                return new(serverUrl, enteredShareId);
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

    private sealed record InteractiveShareReference(Uri ServerUrl, String ShareId);

    private sealed record ResolvedManifest(Uri ServerUrl, String ShareId, ShareManifestContract Manifest, String? BearerToken);
}
