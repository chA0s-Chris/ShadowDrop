// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Queues;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Output;
using ShadowDrop.Queue;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Fetches an existing share manifest by public token or URL and writes a download queue file. Optionally
/// embeds the download credentials to produce a self-contained queue.
/// </summary>
internal sealed class QueueCreateCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(QueueCreateCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Out is null)
        {
            await standardError.WriteLineAsync("Specify the queue output path with --out.");
            return 1;
        }

        if (String.IsNullOrWhiteSpace(options.ShareToken))
        {
            await standardError.WriteLineAsync("Specify a share token or share URL.");
            return 1;
        }

        var hasShareKey = !String.IsNullOrWhiteSpace(options.ShareKey) || options.ShareKeyFile is not null;
        if (options.EmbedSecrets && !hasShareKey)
        {
            await standardError.WriteLineAsync("--embed-secrets requires a share key (--share-key or --share-key-file).");
            return 1;
        }

        if (!options.EmbedSecrets && hasShareKey)
        {
            await standardError.WriteLineAsync("A share key was supplied without --embed-secrets; pass --embed-secrets to embed it.");
            return 1;
        }

        CliResolvedConfiguration configuration;
        try
        {
            configuration = configurationResolver.Resolve(options.ServerUrlOverride, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }

        _ = Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var configuredServerUrl);
        if (!ShareReferenceResolver.TryResolve(options.ShareToken!, configuredServerUrl, out var serverUrl, out var shareToken))
        {
            await standardError.WriteLineAsync("Share token invalid or missing.");
            return 1;
        }

        if (serverUrl is null || (serverUrl.Scheme != Uri.UriSchemeHttp && serverUrl.Scheme != Uri.UriSchemeHttps))
        {
            await standardError.WriteLineAsync("Server URL invalid or missing.");
            return 1;
        }

        try
        {
            AtomicFileWriter.EnsureWritable(options.Out, options.Force);
        }
        catch (AtomicFileException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        QueueCredentials? credentials = null;
        if (options.EmbedSecrets)
        {
            var shareKeyHex = await TryResolveShareKeyHexAsync(options, cancellationToken);
            if (shareKeyHex is null)
            {
                await standardError.WriteLineAsync("Share key invalid or missing.");
                return 1;
            }

            credentials = new()
            {
                ShareKey = shareKeyHex,
                DownloadBearerToken = options.BearerToken
            };
        }

        try
        {
            var manifest = await new ShareManifestClient(httpClient).GetAsync(serverUrl, shareToken, options.BearerToken, cancellationToken);
            var queue = QueueFileBuilder.Build(serverUrl, shareToken, manifest, credentials);
            AtomicFileWriter.WriteAtomic(options.Out, QueueFileParser.Serialize(queue), options.Force, options.EmbedSecrets);

            await standardOut.WriteLineAsync($"queue-file:{options.Out.FullName}");
            return 0;
        }
        catch (DownloadCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (QueueBuildException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (AtomicFileException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private async Task<String?> TryResolveShareKeyHexAsync(QueueCreateCommandOptions options, CancellationToken cancellationToken)
    {
        String? keyText = options.ShareKey;
        if (String.IsNullOrWhiteSpace(keyText) && options.ShareKeyFile is not null)
        {
            try
            {
                keyText = await File.ReadAllTextAsync(options.ShareKeyFile.FullName, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (String.IsNullOrWhiteSpace(keyText))
        {
            return null;
        }

        Byte[]? bytes = null;
        try
        {
            bytes = DownloadCommandHandler.DecodeShareKey(keyText);
            return Convert.ToHexStringLower(bytes);
        }
        catch (DownloadCommandException)
        {
            return null;
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
