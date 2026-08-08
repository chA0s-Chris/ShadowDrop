// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tokens;

internal sealed class ShareInspectCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public ShareInspectCommandHandler(
        CliConfigurationResolver configurationResolver,
        HttpClient httpClient,
        TextWriter standardOut,
        TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public async Task<Int32> ExecuteAsync(ShareInspectCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Guid.TryParse(options.ShareId, out var shareId) || shareId == Guid.Empty)
        {
            await _standardError.WriteLineAsync("Share inspection failed.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(_configurationResolver,
                                                  options.ServerUrlOverride,
                                                  options.AdminTokenOverride,
                                                  _standardError) is not { } configuration)
        {
            return 1;
        }

        try
        {
            var inspection = await new ShareInspectApiClient(_httpClient).InspectAsync(configuration.ServerUrl,
                                                                                       configuration.AdminToken,
                                                                                       shareId,
                                                                                       options.IncludeFilenames,
                                                                                       cancellationToken);
            await ShareInspectResultWriter.WriteAsync(inspection, options.Json, _standardOut);
            return 0;
        }
        catch (ShareInspectCommandException exception)
        {
            await _standardError.WriteLineAsync(exception.NotFound ? "Share not found." : "Share inspection failed.");
            return exception.NotFound ? 6 : 1;
        }
    }
}
