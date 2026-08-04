// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;
using ShadowDrop.Contracts;

internal sealed class ShareListCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(ShareListCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A supplied but valueless `--status` is a caller error: reading it as "every share" would make scripted use
        // non-deterministic whenever a filter variable expands to nothing.
        if (options.PageSize is <= 0 or > ShareListPagination.MaximumPageSize
            || options.Statuses is { Length: 0 }
            || options.Statuses?.Any(status => String.IsNullOrEmpty(status)
                                               || status.Contains(',', StringComparison.Ordinal)
                                               || !ShareListStatuses.CanonicalOrder.Contains(status, StringComparer.Ordinal)) == true)
        {
            await standardError.WriteLineAsync("Share listing failed.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(configurationResolver,
                                                  options.ServerUrlOverride,
                                                  options.AdminTokenOverride,
                                                  standardError) is not { } configuration)
        {
            return 1;
        }

        var selected = (options.Statuses ?? []).ToHashSet(StringComparer.Ordinal);
        var statuses = ShareListStatuses.CanonicalOrder.Where(selected.Contains).ToArray();
        try
        {
            var page = await new ShareListApiClient(httpClient).ListAsync(configuration.ServerUrl,
                                                                          configuration.AdminToken,
                                                                          statuses,
                                                                          options.PageSize,
                                                                          options.Cursor,
                                                                          cancellationToken);
            await ShareListResultWriter.WriteAsync(page, options.Json, standardOut);
            return 0;
        }
        catch (ShareListCommandException)
        {
            await standardError.WriteLineAsync("Share listing failed.");
            return 1;
        }
    }
}
