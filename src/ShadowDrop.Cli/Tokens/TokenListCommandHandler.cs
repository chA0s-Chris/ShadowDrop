// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class TokenListCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(TokenListCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Limit is <= 0)
        {
            await standardError.WriteLineAsync("The --limit value must be positive.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        UploadCredentialCliListResult result;
        try
        {
            result = await new TokenApiClient(httpClient).ListAsync(configuration.ServerUrl,
                                                                    configuration.AdminToken,
                                                                    options.Cursor,
                                                                    options.Limit,
                                                                    cancellationToken);
        }
        catch (TokenCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        if (options.Json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(result,
                                                                      CliJsonSerializerContext.Default.UploadCredentialCliListResult));
            return 0;
        }

        foreach (var credential in result.Credentials)
        {
            await standardOut.WriteLineAsync(TokenOutput.FormatListLine(credential));
        }

        if (result.NextCursor is not null)
        {
            await standardOut.WriteLineAsync($"next-cursor:{result.NextCursor}");
        }

        return 0;
    }
}
