// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Uploads;

internal sealed class ShareCleanupCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(ShareCleanupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await UploadConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.UploadTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        ShareCleanupResultContract result;
        try
        {
            result = await new ShareCleanupApiClient(httpClient).CleanupAsync(configuration.ServerUrl,
                                                                              configuration.UploadToken,
                                                                              cancellationToken);
        }
        catch (ShareCleanupCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        await standardOut.WriteLineAsync(
            $"share-cleanup:candidates-scanned={result.CandidatesScanned} shares-completed={result.SharesCompleted} blobs-deleted={result.BlobsDeleted} blobs-already-missing={result.BlobsAlreadyMissing} failures={result.Failures} skipped={result.Skipped.ToString().ToLowerInvariant()}");
        return 0;
    }
}
