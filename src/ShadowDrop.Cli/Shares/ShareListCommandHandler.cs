// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;
using ShadowDrop.Contracts;

internal sealed class ShareListCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public ShareListCommandHandler(CliConfigurationResolver configurationResolver,
                                   HttpClient httpClient,
                                   TextWriter standardOut,
                                   TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

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
            await _standardError.WriteLineAsync("Share listing failed.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(_configurationResolver,
                                                  options.ServerUrlOverride,
                                                  options.AdminTokenOverride,
                                                  _standardError) is not { } configuration)
        {
            return 1;
        }

        var selected = (options.Statuses ?? []).ToHashSet(StringComparer.Ordinal);
        var statuses = ShareListStatuses.CanonicalOrder.Where(selected.Contains).ToArray();
        try
        {
            var page = await new ShareListApiClient(_httpClient).ListAsync(configuration.ServerUrl,
                                                                           configuration.AdminToken,
                                                                           statuses,
                                                                           options.PageSize,
                                                                           options.Cursor,
                                                                           cancellationToken);
            await ShareListResultWriter.WriteAsync(page, options.Json, _standardOut);
            return 0;
        }
        catch (ShareListCommandException)
        {
            await _standardError.WriteLineAsync("Share listing failed.");
            return 1;
        }
    }
}
