// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Queues;
using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Queue;
using System.Text.Json;

/// <summary>
/// Runs the end-to-end upload workflow: encrypt and upload every file under one share key, create exactly
/// one share when all uploads succeed, and deliver the non-retrievable credentials required to download it.
/// </summary>
internal sealed class UploadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider)
{
    public async Task<Int32> ExecuteAsync(UploadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || ((serverUrl.Scheme != Uri.UriSchemeHttp) && (serverUrl.Scheme != Uri.UriSchemeHttps)))
        {
            await standardError.WriteLineAsync("Server URL invalid or missing.");
            return 1;
        }

        if (String.IsNullOrWhiteSpace(configuration.UploadToken))
        {
            await standardError.WriteLineAsync("Authentication token invalid or missing.");
            return 1;
        }

        // Validate share options and the credential sink before any file I/O or network requests begin.
        var expiration = ShareExpiration.Default;
        if (options.ExpiresIn is not null && !ShareExpiration.TryParse(options.ExpiresIn, out expiration))
        {
            await standardError.WriteLineAsync("Share expiration invalid. Use a value like 7d, 12h, or 30m.");
            return 1;
        }

        if (options.DirectHttp && options.GenerateDownloadToken)
        {
            await standardError.WriteLineAsync("Direct HTTP shares cannot generate a download bearer token.");
            return 1;
        }

        if (options.DirectHttp && options.QueueOut is not null)
        {
            await standardError.WriteLineAsync("Direct HTTP shares do not support queue generation (--queue-out).");
            return 1;
        }

        if (options.EmbedSecrets && options.QueueOut is null)
        {
            await standardError.WriteLineAsync("--embed-secrets requires --queue-out.");
            return 1;
        }

        if (options.SecretsOut is not null)
        {
            try
            {
                AtomicFileWriter.EnsureWritable(options.SecretsOut, options.Force);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                return 1;
            }
        }

        if (options.QueueOut is not null)
        {
            try
            {
                AtomicFileWriter.EnsureWritable(options.QueueOut, options.Force);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                return 1;
            }
        }

        var executor = new UploadCommandExecutor(httpClient);
        var uploadResult = await executor.ExecuteAsync(options.Files, serverUrl, configuration.UploadToken, cancellationToken);
        await ReportUploadProgressAsync(options, uploadResult);

        if (!uploadResult.AllSucceeded || String.IsNullOrWhiteSpace(uploadResult.ShareSecretHex))
        {
            await WriteResultIfJsonAsync(options, BuildResult(UploadCommandStatus.UploadFailed, uploadResult, null, null, null, null));
            return 1;
        }

        var expiresAtUtc = timeProvider.GetUtcNow().Add(expiration);
        var shareRequest = new CreateShareCliRequest(
            expiresAtUtc,
            uploadResult.Files.Select(static result => new CreateShareCliFileRequest(result.UploadedFileId!.Value, result.File.Name)).ToArray(),
            options.DirectHttp,
            options.GenerateDownloadToken,
            options.GenerateDownloadToken ? expiresAtUtc : null);

        CreateShareCliResult shareResult;
        try
        {
            shareResult = await new CreateShareApiClient(httpClient).CreateAsync(serverUrl, configuration.UploadToken, shareRequest, cancellationToken);
        }
        catch (CreateShareCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            await WriteResultIfJsonAsync(options, BuildResult(UploadCommandStatus.ShareCreationFailed, uploadResult, null, null, null, null));
            return 1;
        }

        var shareUrl = ShareDownloadUriFactory.CreateManifestUri(serverUrl, shareResult.ShareToken).AbsoluteUri;

        // A requested download bearer token must be present, otherwise success would be reported without a credential the share requires.
        if (options.GenerateDownloadToken && String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
        {
            await standardError.WriteLineAsync("The share was created but its download bearer token could not be delivered.");
            await WriteResultIfJsonAsync(options,
                                         BuildResult(UploadCommandStatus.CredentialDeliveryFailed, uploadResult, shareResult.ShareId,
                                                     shareResult.ShareToken, shareUrl, null));
            return 1;
        }

        // A share without delivered credentials is unusable, so success is reported only after delivery completes.
        if (options.SecretsOut is not null)
        {
            var document = new CredentialDocument(uploadResult.ShareSecretHex!, shareResult.DownloadBearerToken);
            try
            {
                AtomicFileWriter.WriteAtomic(options.SecretsOut, JsonSerializer.Serialize(document, CliJsonSerializerContext.Default.CredentialDocument),
                                             options.Force, ownerOnly: true);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The share was created but its credentials could not be delivered.");
                await WriteResultIfJsonAsync(options,
                                             BuildResult(UploadCommandStatus.CredentialDeliveryFailed, uploadResult, shareResult.ShareId,
                                                         shareResult.ShareToken, shareUrl, null));
                return 1;
            }
        }

        String? queueFilePath = null;
        if (options.QueueOut is not null)
        {
            try
            {
                var queueCredentials = options.EmbedSecrets
                    ? new QueueCredentials
                    {
                        ShareKey = uploadResult.ShareSecretHex!,
                        DownloadBearerToken = shareResult.DownloadBearerToken
                    }
                    : null;
                var manifest = await new ShareManifestClient(httpClient).GetAsync(serverUrl, shareResult.ShareToken, shareResult.DownloadBearerToken,
                                                                                  cancellationToken);
                var queue = QueueFileBuilder.Build(serverUrl, shareResult.ShareToken, manifest, queueCredentials);
                AtomicFileWriter.WriteAtomic(options.QueueOut, QueueFileParser.Serialize(queue), options.Force, ownerOnly: options.EmbedSecrets);
                queueFilePath = options.QueueOut.FullName;
            }
            catch (Exception exception) when (exception is DownloadCommandException or QueueBuildException or AtomicFileException)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The share was created but the queue file could not be generated.");

                // Still deliver the credentials so they are not lost, but report the failed stage and a non-zero exit.
                await EmitResultAsync(options, UploadCommandStatus.QueueWriteFailed, uploadResult, shareResult, shareUrl, queueFile: null);
                return 1;
            }
        }

        await EmitResultAsync(options, UploadCommandStatus.Succeeded, uploadResult, shareResult, shareUrl, queueFilePath);
        return 0;
    }

    private UploadCommandResult BuildResult(String status,
                                            UploadExecutionResult uploadResult,
                                            Guid? shareId,
                                            String? shareToken,
                                            String? shareUrl,
                                            UploadCredentials? credentials,
                                            String? secretsFile = null,
                                            String? queueFile = null) =>
        new(status,
            uploadResult.UploadedFileIds.Select(static id => id.ToString()).ToArray(),
            shareId,
            shareToken,
            shareUrl,
            credentials,
            secretsFile,
            queueFile);

    private async Task EmitResultAsync(UploadCommandOptions options,
                                       String status,
                                       UploadExecutionResult uploadResult,
                                       CreateShareCliResult shareResult,
                                       String shareUrl,
                                       String? queueFile)
    {
        var credentialsToFile = options.SecretsOut is not null;

        if (options.Json)
        {
            var credentials = credentialsToFile ? null : new UploadCredentials(uploadResult.ShareSecretHex!, shareResult.DownloadBearerToken);
            var secretsFile = credentialsToFile ? options.SecretsOut!.FullName : null;
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(
                                                 BuildResult(status, uploadResult, shareResult.ShareId, shareResult.ShareToken, shareUrl, credentials,
                                                             secretsFile, queueFile),
                                                 CliJsonSerializerContext.Default.UploadCommandResult));
            return;
        }

        await standardOut.WriteLineAsync($"share-url:{shareUrl}");
        if (credentialsToFile)
        {
            await standardOut.WriteLineAsync($"secrets-file:{options.SecretsOut!.FullName}");
        }
        else
        {
            await standardOut.WriteLineAsync($"share-key:{uploadResult.ShareSecretHex}");
            if (!String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
            {
                await standardOut.WriteLineAsync($"download-bearer-token:{shareResult.DownloadBearerToken}");
            }
        }

        if (queueFile is not null)
        {
            await standardOut.WriteLineAsync($"queue-file:{queueFile}");
        }
    }

    private async Task ReportUploadProgressAsync(UploadCommandOptions options, UploadExecutionResult uploadResult)
    {
        for (var index = 0; index < uploadResult.Files.Count; index++)
        {
            var fileResult = uploadResult.Files[index];
            if (fileResult.UploadedFileId is not null)
            {
                await standardError.WriteLineAsync($"Uploaded file {index + 1} of {options.Files.Length}.");
            }
            else
            {
                await standardError.WriteLineAsync($"File {index + 1} failed: {fileResult.ErrorMessage}");
            }
        }
    }

    private async Task WriteResultIfJsonAsync(UploadCommandOptions options, UploadCommandResult result)
    {
        if (options.Json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(result, CliJsonSerializerContext.Default.UploadCommandResult));
        }
    }
}
