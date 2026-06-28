// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Output;
using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Uploads;
using System.Text.Json;

/// <summary>
/// Lower-level share creation: builds a share from previously uploaded file IDs (in the supplied order) and
/// delivers the public share token/URL plus any generated download bearer token. It never requires the
/// plaintext share key because the server does not receive it.
/// </summary>
internal sealed class ShareCreateCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider)
{
    public async Task<Int32> ExecuteAsync(ShareCreateCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<Guid> fileIdList = [];
        foreach (var fileIdText in options.FileIds)
        {
            if (!Guid.TryParse(fileIdText, out var fileId) || fileId == Guid.Empty)
            {
                await standardError.WriteLineAsync("File id invalid or missing.");
                return 1;
            }

            fileIdList.Add(fileId);
        }

        // Resolve display-name overrides before contacting the server so ambiguous or malformed input fails fast.
        if (!DisplayNameResolver.TryResolveForShareCreate(fileIdList, options.DisplayNameMappings, out var displayNameOverrides,
                                                          out var displayNameError))
        {
            await standardError.WriteLineAsync(displayNameError);
            return 1;
        }

        var files = fileIdList.Select(fileId => new CreateShareCliFileRequest(fileId, displayNameOverrides.GetValueOrDefault(fileId))).ToList();

        if (await UploadConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.UploadTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        var serverUrl = configuration.ServerUrl;

        if (!ShareOptions.TryValidate(options.ExpiresIn, options.DirectHttp, options.GenerateDownloadToken, out var expiration, out var optionError))
        {
            await standardError.WriteLineAsync(optionError);
            return 1;
        }

        if (options.SecretsOut is not null)
        {
            // The only secret this command can deliver is the download bearer token.
            if (!options.GenerateDownloadToken)
            {
                await standardError.WriteLineAsync("--secrets-out requires --download-token; there are no other secrets to write.");
                return 1;
            }

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

        var expiresAtUtc = timeProvider.GetUtcNow().Add(expiration);
        var shareRequest = new CreateShareCliRequest(expiresAtUtc,
                                                     files,
                                                     options.DirectHttp,
                                                     options.GenerateDownloadToken,
                                                     options.GenerateDownloadToken ? expiresAtUtc : null);

        // Report canonical GUID strings so the JSON surface matches upload / upload raw regardless of input formatting.
        var fileIds = files.Select(static file => file.FileId.ToString()).ToArray();

        CreateShareCliResult shareResult;
        try
        {
            shareResult = await new CreateShareApiClient(httpClient).CreateAsync(serverUrl, configuration.UploadToken, shareRequest, cancellationToken);
        }
        catch (CreateShareCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            if (options.Json)
            {
                await UploadResultWriter.WriteAsync(standardOut,
                                                    new(UploadCommandStatus.ShareCreationFailed, fileIds, null, null, null, null, null, null));
            }

            return 1;
        }

        var shareUrl = ShareDownloadUriFactory.CreateManifestUri(serverUrl, shareResult.ShareToken).AbsoluteUri;

        // A requested download bearer token must be present, otherwise success would omit a credential the share requires.
        if (options.GenerateDownloadToken && String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
        {
            await standardError.WriteLineAsync("The share was created but its download bearer token could not be delivered.");
            if (options.Json)
            {
                await UploadResultWriter.WriteAsync(standardOut,
                                                    new(UploadCommandStatus.CredentialDeliveryFailed, fileIds, shareResult.ShareId, shareResult.ShareToken,
                                                        shareUrl, null, null, null));
            }
            else
            {
                // The share exists; report its URL so callers can identify it for cleanup/retry. Never the bearer token.
                await standardOut.WriteLineAsync($"share-url:{shareUrl}");
            }

            return 1;
        }

        if (options.SecretsOut is not null)
        {
            var document = new CredentialDocument(null, shareResult.DownloadBearerToken);
            try
            {
                AtomicFileWriter.WriteAtomic(options.SecretsOut, JsonSerializer.Serialize(document, CliJsonSerializerContext.Default.CredentialDocument),
                                             options.Force, true);
            }
            catch (AtomicFileException exception)
            {
                await standardError.WriteLineAsync(exception.Message);
                await standardError.WriteLineAsync("The share was created but its download bearer token could not be delivered.");
                if (options.Json)
                {
                    await UploadResultWriter.WriteAsync(standardOut,
                                                        new(UploadCommandStatus.CredentialDeliveryFailed, fileIds, shareResult.ShareId, shareResult.ShareToken,
                                                            shareUrl, null, null, null));
                }
                else
                {
                    await standardOut.WriteLineAsync($"share-url:{shareUrl}");
                }

                return 1;
            }
        }

        await EmitSuccessAsync(options, fileIds, shareResult, shareUrl);
        return 0;
    }

    private async Task EmitSuccessAsync(ShareCreateCommandOptions options, IReadOnlyList<String> fileIds, CreateShareCliResult shareResult, String shareUrl)
    {
        if (options.Json)
        {
            UploadCredentials? credentials = null;
            if (options.SecretsOut is null && !String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
            {
                credentials = new(null, shareResult.DownloadBearerToken);
            }

            await UploadResultWriter.WriteAsync(standardOut,
                                                new(UploadCommandStatus.Succeeded, fileIds, shareResult.ShareId, shareResult.ShareToken, shareUrl, credentials,
                                                    options.SecretsOut?.FullName, null));
            return;
        }

        await standardOut.WriteLineAsync($"share-url:{shareUrl}");
        if (options.SecretsOut is not null)
        {
            await standardOut.WriteLineAsync($"secrets-file:{options.SecretsOut.FullName}");
            return;
        }

        if (!String.IsNullOrWhiteSpace(shareResult.DownloadBearerToken))
        {
            await standardOut.WriteLineAsync($"download-bearer-token:{shareResult.DownloadBearerToken}");
        }
    }
}
