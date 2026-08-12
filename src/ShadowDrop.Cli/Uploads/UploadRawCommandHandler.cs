// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Uploads.Progress;
using System.Text.Json;

/// <summary>
/// Lower-level encrypted intake: uploads files under one share key and reports the uploaded file IDs plus the
/// non-retrievable share key, without creating a share. Intended for scripting and recovery composition with
/// <c>share create</c>.
/// </summary>
internal sealed class UploadRawCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly IUploadProgressReporterFactory _uploadProgressReporterFactory;

    public UploadRawCommandHandler(CliConfigurationResolver configurationResolver,
                                   HttpClient httpClient,
                                   TextWriter standardOut,
                                   TextWriter standardError,
                                   IUploadProgressReporterFactory uploadProgressReporterFactory)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
        _uploadProgressReporterFactory = uploadProgressReporterFactory;
    }

    public async Task<Int32> ExecuteAsync(UploadRawCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var progressReporter = _uploadProgressReporterFactory.Create(options.Json);
        var planningResult = LocalUploadPlanner.Create(options.Files.Select(UploadSelection.FromCommandLine));
        if (!planningResult.IsValid)
        {
            var failedResult = await LocalUploadPlanner.ReportFailureAsync(planningResult, options.Files.Length, progressReporter, cancellationToken);
            await EmitPlanningFailureAsync(options, failedResult);
            return 1;
        }

        var localPlan = planningResult.Plan ?? throw new InvalidOperationException("A valid planning result must contain a plan.");

        if (options.SecretsOut is not null)
        {
            try
            {
                AtomicFileWriter.EnsureWritable(options.SecretsOut, options.Force);
            }
            catch (AtomicFileException exception)
            {
                await _standardError.WriteLineAsync(exception.Message);
                return 1;
            }
        }

        if (await UploadConfiguration.ResolveAsync(_configurationResolver, options.ServerUrlOverride, options.UploadTokenOverride, _standardError)
            is not { } configuration)
        {
            return 1;
        }

        var executor = new UploadCommandExecutor(_httpClient);
        var uploadResult =
            await executor.ExecuteAsync(localPlan,
                                        configuration.ServerUrl,
                                        configuration.UploadToken,
                                        progressReporter,
                                        cancellationToken);

        var uploadedFileIds = uploadResult.UploadedFileIds.Select(static id => id.ToString()).ToArray();

        if (!uploadResult.AllSucceeded || String.IsNullOrWhiteSpace(uploadResult.ShareSecretHex))
        {
            if (options.Json)
            {
                await UploadResultWriter.WriteAsync(_standardOut,
                                                    new(UploadCommandStatus.UploadFailed,
                                                        uploadedFileIds,
                                                        null,
                                                        null,
                                                        null,
                                                        null,
                                                        null,
                                                        null,
                                                        null,
                                                        uploadResult.Failures.Count > 0 ? uploadResult.Failures : null));
            }
            else
            {
                // Report the file IDs that did upload so scripts can still capture/recover them; never the share key on failure.
                foreach (var fileId in uploadedFileIds)
                {
                    await _standardOut.WriteLineAsync($"file-id:{fileId}");
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
                await _standardError.WriteLineAsync(exception.Message);
                await _standardError.WriteLineAsync("The files were uploaded but the share key could not be delivered.");
                if (options.Json)
                {
                    await UploadResultWriter.WriteAsync(_standardOut,
                                                        new(UploadCommandStatus.CredentialDeliveryFailed, uploadedFileIds, null, null, null, null, null, null));
                }
                else
                {
                    // The uploads already happened; report their IDs so callers can recover/clean them up. Never the share key.
                    foreach (var fileId in uploadedFileIds)
                    {
                        await _standardOut.WriteLineAsync($"file-id:{fileId}");
                    }
                }

                return 1;
            }
        }

        await EmitSuccessAsync(options, uploadedFileIds, uploadResult.ShareSecretHex);
        return 0;
    }

    private async Task EmitPlanningFailureAsync(UploadRawCommandOptions options, UploadExecutionResult uploadResult)
    {
        if (!options.Json)
        {
            return;
        }

        await UploadResultWriter.WriteAsync(_standardOut,
                                            new(UploadCommandStatus.UploadFailed,
                                                [],
                                                null,
                                                null,
                                                null,
                                                null,
                                                null,
                                                null,
                                                null,
                                                uploadResult.Failures));
    }

    private async Task EmitSuccessAsync(UploadRawCommandOptions options, IReadOnlyList<String> uploadedFileIds, String shareKey)
    {
        if (options.Json)
        {
            var credentials = options.SecretsOut is null ? new UploadCredentials(shareKey, null) : null;
            await UploadResultWriter.WriteAsync(_standardOut,
                                                new(UploadCommandStatus.Succeeded, uploadedFileIds, null, null, null, credentials, options.SecretsOut?.FullName,
                                                    null));
            return;
        }

        foreach (var fileId in uploadedFileIds)
        {
            await _standardOut.WriteLineAsync($"file-id:{fileId}");
        }

        if (options.SecretsOut is not null)
        {
            await _standardOut.WriteLineAsync($"secrets-file:{options.SecretsOut.FullName}");
            return;
        }

        await _standardOut.WriteLineAsync($"share-key:{shareKey}");
    }
}
