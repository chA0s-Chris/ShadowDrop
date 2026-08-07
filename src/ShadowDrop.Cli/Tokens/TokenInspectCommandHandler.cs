// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class TokenInspectCommandHandler
{
    private readonly CliConfigurationResolver _configurationResolver;
    private readonly HttpClient _httpClient;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;

    public TokenInspectCommandHandler(CliConfigurationResolver configurationResolver,
                                      HttpClient httpClient,
                                      TextWriter standardOut,
                                      TextWriter standardError)
    {
        _configurationResolver = configurationResolver;
        _httpClient = httpClient;
        _standardOut = standardOut;
        _standardError = standardError;
    }

    public async Task<Int32> ExecuteAsync(TokenInspectCommandOptions options, CancellationToken cancellationToken)
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

        UploadCredentialCliProjection credential;
        try
        {
            credential = await new TokenApiClient(_httpClient).InspectAsync(configuration.ServerUrl,
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
            await _standardOut.WriteLineAsync(JsonSerializer.Serialize(credential,
                                                                       CliJsonSerializerContext.Default.UploadCredentialCliProjection));
            return 0;
        }

        await TokenOutput.WriteDetailsAsync(_standardOut, credential);
        return 0;
    }
}
