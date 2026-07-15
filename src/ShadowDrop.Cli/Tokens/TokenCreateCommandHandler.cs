// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Uploads;
using System.Text.Json;

internal sealed class TokenCreateCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider)
{
    private const Int32 MaxNameLength = 100;

    public async Task<Int32> ExecuteAsync(TokenCreateCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Mirrors the server-side rule so obviously invalid names fail fast with a specific message instead of
        // the generic 400 response.
        var name = options.Name?.Trim();
        if (String.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Any(Char.IsControl))
        {
            await standardError.WriteLineAsync(
                "Credential name invalid or missing. Provide --name with 1 to 100 characters and no control characters.");
            return 1;
        }

        DateTimeOffset? expiresAtUtc = null;
        if (options.ExpiresIn is not null)
        {
            if (!ShareExpiration.TryParse(options.ExpiresIn, out var duration))
            {
                await standardError.WriteLineAsync("Expiration invalid. Use <amount><unit> such as 30d, 12h, or 45m.");
                return 1;
            }

            var now = timeProvider.GetUtcNow();
            if (duration > DateTimeOffset.MaxValue - now)
            {
                await standardError.WriteLineAsync("Expiration invalid. Use <amount><unit> such as 30d, 12h, or 45m.");
                return 1;
            }

            expiresAtUtc = now.Add(duration);
        }

        if (options.MaxFileBytes is <= 0)
        {
            await standardError.WriteLineAsync("The --max-file-bytes value must be positive.");
            return 1;
        }

        if (options.MaxShareBytes is <= 0)
        {
            await standardError.WriteLineAsync("The --max-share-bytes value must be positive.");
            return 1;
        }

        if (await AdminConfiguration.ResolveAsync(configurationResolver, options.ServerUrlOverride, options.AdminTokenOverride, standardError)
            is not { } configuration)
        {
            return 1;
        }

        CreateUploadCredentialCliResult result;
        try
        {
            result = await new TokenApiClient(httpClient).CreateAsync(configuration.ServerUrl,
                                                                      configuration.AdminToken,
                                                                      new(name,
                                                                          expiresAtUtc,
                                                                          options.MaxFileBytes,
                                                                          options.MaxShareBytes),
                                                                      cancellationToken);
        }
        catch (TokenCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        // The plaintext token is disclosed exactly once here, on the selected stdout destination only.
        if (options.Json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(result,
                                                                      CliJsonSerializerContext.Default.CreateUploadCredentialCliResult));
            return 0;
        }

        await standardOut.WriteLineAsync($"credential-id:{result.Credential.CredentialId}");
        await standardOut.WriteLineAsync($"token:{result.Token}");
        await standardError.WriteLineAsync("Store the token now: it is displayed once and cannot be recovered.");
        return 0;
    }
}
