// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class TokenListCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public TokenListCommandHandler(CliConfigurationResolver configurationResolver,
                                   HttpClient httpClient,
                                   TextWriter standardOut,
                                   TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public async Task<Int32> ExecuteAsync(TokenListCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Limit is <= 0)
        {
            await _standardError.WriteLineAsync("The --limit value must be positive.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(_configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, _standardError)
            is not { } configuration)
        {
            return 1;
        }

        UploadCredentialCliListResult result;
        try
        {
            result = await new TokenApiClient(_httpClient).ListAsync(configuration.ServerUrl,
                                                                     configuration.AdminToken,
                                                                     options.Cursor,
                                                                     options.Limit,
                                                                     cancellationToken);
        }
        catch (TokenCommandException exception)
        {
            await _standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        if (options.Json)
        {
            await _standardOut.WriteLineAsync(JsonSerializer.Serialize(result,
                                                                       CliJsonSerializerContext.Default.UploadCredentialCliListResult));
            return 0;
        }

        foreach (var credential in result.Credentials)
        {
            await _standardOut.WriteLineAsync(TokenOutput.FormatListLine(credential));
        }

        if (result.NextCursor is not null)
        {
            await _standardOut.WriteLineAsync($"next-cursor:{result.NextCursor}");
        }

        return 0;
    }
}
