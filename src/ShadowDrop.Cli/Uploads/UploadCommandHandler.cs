// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Queues;
using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Cli.Uploads.Progress;
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
    TimeProvider timeProvider,
    IUploadProgressReporterFactory uploadProgressReporterFactory,
    CliBannerWriter bannerWriter)
{
    public async Task<Int32> ExecuteAsync(UploadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Resolve recipient-facing display names before any file I/O or network requests so malformed or ambiguous
        // input fails fast with a clear error.
        if (!DisplayNameResolver.TryResolveForUpload(options.Files, options.DisplayName, options.DisplayNameMappings,
                                                     out var displayNameOverrides, out var displayNameError))
        {
            await standardError.WriteLineAsync(displayNameError);
            return 1;
        }

        if (await UploadConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.UploadTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        var serverUrl = configuration.ServerUrl;

        // Validate share options and the credential sink before any file I/O or network requests begin.
        if (!ShareOptions.TryValidate(options.ExpiresIn, options.DirectHttp, options.GenerateDownloadToken, out var expiration, out var optionError))
        {
            await standardError.WriteLineAsync(optionError);
            return 1;
        }

        if (options.DirectHttp && options.QueueOut is not null)
        {
            await standardError.WriteLineAsync("Direct HTTP shares do not support queue generation (--queue-out).");
            return 1;
        }

        if (options.DirectHttp && options.SecretsOut is not null)
        {
            await standardError.WriteLineAsync("Direct HTTP shares do not support writing secrets to a separate file (--secrets-out).");
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
        var uploadResult =
            await executor.ExecuteAsync(options.Files,
                                        serverUrl,
                                        configuration.UploadToken,
                                        uploadProgressReporterFactory.Create(options.Json),
                                        cancellationToken);

        if (!uploadResult.AllSucceeded || String.IsNullOrWhiteSpace(uploadResult.ShareSecretHex))
        {
            await WriteResultIfJsonAsync(options, BuildResult(UploadCommandStatus.UploadFailed, uploadResult, null, null, null, null));
            return 1;
        }

        var expiresAtUtc = timeProvider.GetUtcNow().Add(expiration);
        var shareRequest = new CreateShareCliRequest(
            expiresAtUtc,
            uploadResult.Files.Select(result => new CreateShareCliFileRequest(result.UploadedFileId!.Value,
                                                                              ResolveDisplayName(displayNameOverrides, result.File))).ToArray(),
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
            if (!options.Json)
            {
                await standardOut.WriteLineAsync($"share-url:{shareUrl}");
            }

            return 1;
        }

        // A share without delivered credentials is unusable, so success is reported only after delivery completes.
        if (options.SecretsOut is not null)
        {
            var document = new CredentialDocument(uploadResult.ShareSecretHex!, shareResult.DownloadBearerToken);
            try
            {
                AtomicFileWriter.WriteAtomic(options.SecretsOut, JsonSerializer.Serialize(document, CliJsonSerializerContext.Default.CredentialDocument),
                                             options.Force, true);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The share was created but its credentials could not be delivered.");
                await WriteResultIfJsonAsync(options,
                                             BuildResult(UploadCommandStatus.CredentialDeliveryFailed, uploadResult, shareResult.ShareId,
                                                         shareResult.ShareToken, shareUrl, null));
                if (!options.Json)
                {
                    await standardOut.WriteLineAsync($"share-url:{shareUrl}");
                }

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
                AtomicFileWriter.WriteAtomic(options.QueueOut, QueueFileParser.Serialize(queue), options.Force, options.EmbedSecrets);
                queueFilePath = options.QueueOut.FullName;
            }
            catch (Exception exception) when (exception is DownloadCommandException or QueueBuildException or AtomicFileException)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The share was created but the queue file could not be generated.");

                // Still deliver the credentials so they are not lost, but report the failed stage and a non-zero exit.
                await EmitResultAsync(options, serverUrl, UploadCommandStatus.QueueWriteFailed, uploadResult, shareResult, shareUrl, null,
                                      displayNameOverrides);
                return 1;
            }
        }

        // The success output (JSON or share-url:/share-key:/queue-file:/... lines) is a parseable, script-consumed
        // stdout contract; the banner goes to stderr so it never corrupts it. Failure paths above never call it.
        await bannerWriter.WriteToStandardErrorAsync(standardError, cancellationToken);
        await EmitResultAsync(options, serverUrl, UploadCommandStatus.Succeeded, uploadResult, shareResult, shareUrl, queueFilePath,
                              displayNameOverrides);
        return 0;
    }

    private static IReadOnlyList<DirectHttpDownload> BuildDirectHttpDownloads(Uri serverUrl,
                                                                              UploadExecutionResult uploadResult,
                                                                              String shareToken,
                                                                              IReadOnlyDictionary<String, String> displayNameOverrides)
    {
        return uploadResult.Files
                           .Where(static result => result.UploadedFileId is not null)
                           .Select(result =>
                           {
                               var fileId = result.UploadedFileId!.Value;
                               var fileName = ResolveDisplayName(displayNameOverrides, result.File) ?? result.File.Name;
                               var downloadUrl = DirectHttpDownloadUrlFactory.Create(serverUrl, shareToken, fileId, uploadResult.ShareSecretHex!);
                               var curlCommand = DirectHttpCurlCommandFactory.Create(serverUrl, shareToken, fileId, uploadResult.ShareSecretHex!,
                                                                                     fileName);
                               return new DirectHttpDownload(fileId.ToString(), fileName, downloadUrl, curlCommand);
                           })
                           .ToArray();
    }

    private static String? ResolveDisplayName(IReadOnlyDictionary<String, String> displayNameOverrides, FileInfo file) =>
        displayNameOverrides.GetValueOrDefault(file.FullName);

    private UploadCommandResult BuildResult(String status,
                                            UploadExecutionResult uploadResult,
                                            Guid? shareId,
                                            String? shareToken,
                                            String? shareUrl,
                                            UploadCredentials? credentials,
                                            String? secretsFile = null,
                                            String? queueFile = null,
                                            IReadOnlyList<DirectHttpDownload>? directHttpDownloads = null) =>
        new(status,
            uploadResult.UploadedFileIds.Select(static id => id.ToString()).ToArray(),
            shareId,
            shareToken,
            shareUrl,
            credentials,
            secretsFile,
            queueFile,
            directHttpDownloads,
            uploadResult.Failures.Count > 0 ? uploadResult.Failures : null);

    private async Task EmitResultAsync(UploadCommandOptions options,
                                       Uri serverUrl,
                                       String status,
                                       UploadExecutionResult uploadResult,
                                       CreateShareCliResult shareResult,
                                       String shareUrl,
                                       String? queueFile,
                                       IReadOnlyDictionary<String, String> displayNameOverrides)
    {
        var credentialsToFile = options.SecretsOut is not null;
        var directHttpDownloads = options.DirectHttp
            ? BuildDirectHttpDownloads(serverUrl, uploadResult, shareResult.ShareToken, displayNameOverrides)
            : null;

        if (options.Json)
        {
            var credentials = credentialsToFile ? null : new UploadCredentials(uploadResult.ShareSecretHex!, shareResult.DownloadBearerToken);
            var secretsFile = credentialsToFile ? options.SecretsOut!.FullName : null;
            await UploadResultWriter.WriteAsync(standardOut,
                                                BuildResult(status, uploadResult, shareResult.ShareId, shareResult.ShareToken, shareUrl, credentials,
                                                            secretsFile, queueFile, directHttpDownloads));
            return;
        }

        await standardOut.WriteLineAsync($"share-url:{shareUrl}");
        if (directHttpDownloads is not null)
        {
            await WriteDirectHttpDownloadsAsync(directHttpDownloads);
        }
        else if (credentialsToFile)
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
            if (!options.EmbedSecrets)
            {
                var keySource = options.SecretsOut is not null
                    ? $"the 'shareKey' value from {options.SecretsOut.FullName}"
                    : "the 'share-key:' value shown above";
                await standardError.WriteLineAsync(
                    $"Note: the queue is secret-free. Keep {keySource} and pass it via --share-key to download the queue, "
                    + "or re-run with --embed-secrets for a self-contained queue.");
            }
        }
    }

    private async Task WriteDirectHttpDownloadsAsync(IReadOnlyList<DirectHttpDownload> directHttpDownloads)
    {
        if (directHttpDownloads.Count == 1)
        {
            await standardOut.WriteLineAsync($"download-url:{directHttpDownloads[0].DownloadUrl}");
            await standardOut.WriteLineAsync($"curl-command:{directHttpDownloads[0].CurlCommand}");
            return;
        }

        foreach (var directHttpDownload in directHttpDownloads)
        {
            await standardOut.WriteLineAsync($"download-url:{directHttpDownload.FileId}:{directHttpDownload.DownloadUrl}");
        }

        foreach (var directHttpDownload in directHttpDownloads)
        {
            await standardOut.WriteLineAsync($"curl-command:{directHttpDownload.FileId}:{directHttpDownload.CurlCommand}");
        }
    }

    private async Task WriteResultIfJsonAsync(UploadCommandOptions options, UploadCommandResult result)
    {
        if (options.Json)
        {
            await UploadResultWriter.WriteAsync(standardOut, result);
        }
    }
}
