// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;

internal sealed class ShareRevokeCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(ShareRevokeCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Guid.TryParse(options.ShareId, out var shareId) || shareId == Guid.Empty)
        {
            await standardError.WriteLineAsync("Share id invalid or missing.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        try
        {
            await new RevokeShareApiClient(httpClient).RevokeAsync(configuration.ServerUrl, configuration.AdminToken, shareId, cancellationToken);
        }
        catch (RevokeShareCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        await standardOut.WriteLineAsync($"share-revoked:{shareId}");
        return 0;
    }
}
