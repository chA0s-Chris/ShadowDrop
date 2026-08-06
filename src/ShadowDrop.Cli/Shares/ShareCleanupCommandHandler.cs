// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;

internal sealed class ShareCleanupCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public ShareCleanupCommandHandler(CliConfigurationResolver configurationResolver,
                                      HttpClient httpClient,
                                      TextWriter standardOut,
                                      TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public async Task<Int32> ExecuteAsync(ShareCleanupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await AdminConfiguration.ResolveAsync(_configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, _standardError)
            is not { } configuration)
        {
            return 1;
        }

        ShareCleanupResultContract result;
        try
        {
            result = await new ShareCleanupApiClient(_httpClient).CleanupAsync(configuration.ServerUrl,
                                                                               configuration.AdminToken,
                                                                               cancellationToken);
        }
        catch (ShareCleanupCommandException exception)
        {
            await _standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        await _standardOut.WriteLineAsync(
            $"share-cleanup:candidates-scanned={result.CandidatesScanned} shares-completed={result.SharesCompleted} blobs-deleted={result.BlobsDeleted} blobs-already-missing={result.BlobsAlreadyMissing} failures={result.Failures} sweep-candidates-inspected={result.SweepCandidatesInspected} sweep-uploads-deleted={result.SweepUploadsDeleted} sweep-blobs-already-missing={result.SweepBlobsAlreadyMissing} sweep-failures={result.SweepFailures} skipped={result.Skipped.ToString().ToLowerInvariant()}");
        return 0;
    }
}
