// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class TokenRevokeCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    public async Task<Int32> ExecuteAsync(TokenRevokeCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Guid.TryParse(options.CredentialId, out var credentialId) || credentialId == Guid.Empty)
        {
            await standardError.WriteLineAsync("Credential id invalid or missing.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        try
        {
            await new TokenApiClient(httpClient).RevokeAsync(configuration.ServerUrl,
                                                             configuration.AdminToken,
                                                             credentialId,
                                                             cancellationToken);
        }
        catch (TokenCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        if (options.Json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(new(credentialId),
                                                                      CliJsonSerializerContext.Default.TokenRevokeCliResult));
            return 0;
        }

        await standardOut.WriteLineAsync($"token-revoked:{credentialId}");
        return 0;
    }
}
