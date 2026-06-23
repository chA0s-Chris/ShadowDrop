// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Results;
using System.Text.Json;

/// <summary>
/// Lower-level encrypted intake: uploads files under one share key and reports the uploaded file IDs plus the
/// non-retrievable share key, without creating a share. Intended for scripting and recovery composition with
/// <c>share create</c>.
/// </summary>
internal sealed class UploadRawCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(UploadRawCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await UploadConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.UploadTokenOverride, standardError)
            is not { } configuration)
        {
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

        var executor = new UploadCommandExecutor(httpClient);
        var uploadResult = await executor.ExecuteAsync(options.Files, configuration.ServerUrl, configuration.UploadToken, cancellationToken);
        await UploadProgressReporter.ReportAsync(standardError, uploadResult, options.Files.Length);

        var uploadedFileIds = uploadResult.UploadedFileIds.Select(static id => id.ToString()).ToArray();

        if (!uploadResult.AllSucceeded || String.IsNullOrWhiteSpace(uploadResult.ShareSecretHex))
        {
            if (options.Json)
            {
                await UploadResultWriter.WriteAsync(standardOut, new(UploadCommandStatus.UploadFailed, uploadedFileIds, null, null, null, null, null, null));
            }
            else
            {
                // Report the file IDs that did upload so scripts can still capture/recover them; never the share key on failure.
                foreach (var fileId in uploadedFileIds)
                {
                    await standardOut.WriteLineAsync($"file-id:{fileId}");
                }
            }

            return 1;
        }

        // The share key is the only non-retrievable credential; deliver it before reporting success.
        if (options.SecretsOut is not null)
        {
            var document = new CredentialDocument(uploadResult.ShareSecretHex, null);
            try
            {
                AtomicFileWriter.WriteAtomic(options.SecretsOut, JsonSerializer.Serialize(document, CliJsonSerializerContext.Default.CredentialDocument),
                                             options.Force, true);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The files were uploaded but the share key could not be delivered.");
                if (options.Json)
                {
                    await UploadResultWriter.WriteAsync(standardOut,
                                                        new(UploadCommandStatus.CredentialDeliveryFailed, uploadedFileIds, null, null, null, null, null, null));
                }
                else
                {
                    // The uploads already happened; report their IDs so callers can recover/clean them up. Never the share key.
                    foreach (var fileId in uploadedFileIds)
                    {
                        await standardOut.WriteLineAsync($"file-id:{fileId}");
                    }
                }

                return 1;
            }
        }

        await EmitSuccessAsync(options, uploadedFileIds, uploadResult.ShareSecretHex);
        return 0;
    }

    private async Task EmitSuccessAsync(UploadRawCommandOptions options, IReadOnlyList<String> uploadedFileIds, String shareKey)
    {
        if (options.Json)
        {
            var credentials = options.SecretsOut is null ? new UploadCredentials(shareKey, null) : null;
            await UploadResultWriter.WriteAsync(standardOut,
                                                new(UploadCommandStatus.Succeeded, uploadedFileIds, null, null, null, credentials, options.SecretsOut?.FullName,
                                                    null));
            return;
        }

        foreach (var fileId in uploadedFileIds)
        {
            await standardOut.WriteLineAsync($"file-id:{fileId}");
        }

        if (options.SecretsOut is not null)
        {
            await standardOut.WriteLineAsync($"secrets-file:{options.SecretsOut.FullName}");
            return;
        }

        await standardOut.WriteLineAsync($"share-key:{shareKey}");
    }
}
