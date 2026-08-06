// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class TokenRevokeCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public TokenRevokeCommandHandler(CliConfigurationResolver configurationResolver,
                                     HttpClient httpClient,
                                     TextWriter standardOut,
                                     TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public async Task<Int32> ExecuteAsync(TokenRevokeCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Guid.TryParse(options.CredentialId, out var credentialId) || credentialId == Guid.Empty)
        {
            await _standardError.WriteLineAsync("Credential id invalid or missing.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(_configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, _standardError)
            is not { } configuration)
        {
            return 1;
        }

        try
        {
            await new TokenApiClient(_httpClient).RevokeAsync(configuration.ServerUrl,
                                                              configuration.AdminToken,
                                                              credentialId,
                                                              cancellationToken);
        }
        catch (TokenCommandException exception)
        {
            await _standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        if (options.Json)
        {
            await _standardOut.WriteLineAsync(JsonSerializer.Serialize(new(credentialId),
                                                                       CliJsonSerializerContext.Default.TokenRevokeCliResult));
            return 0;
        }

        await _standardOut.WriteLineAsync($"token-revoked:{credentialId}");
        return 0;
    }
}
