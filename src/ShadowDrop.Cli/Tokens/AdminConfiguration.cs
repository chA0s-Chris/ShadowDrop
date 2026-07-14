// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

/// <summary>
/// Resolves and validates the server URL and admin token for administrative commands, emitting the error and
/// returning <see langword="null"/> on failure. The admin token never falls back to the upload-token setting.
/// </summary>
internal static class AdminConfiguration
{
    public const String MissingAdminTokenError =
        "Admin token missing. Provide --admin-token, set SHADOWDROP_ADMIN_TOKEN, or add adminToken to the CLI config file.";

    public static async Task<ResolvedAdminConfiguration?> ResolveAsync(CliConfigurationResolver configurationResolver,
                                                                       String? serverUrlOverride,
                                                                       String? adminTokenOverride,
                                                                       TextWriter standardError)
    {
        CliResolvedAdminConfiguration configuration;
        try
        {
            configuration = configurationResolver.ResolveAdmin(serverUrlOverride, adminTokenOverride);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return null;
        }

        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || (serverUrl.Scheme != Uri.UriSchemeHttp && serverUrl.Scheme != Uri.UriSchemeHttps))
        {
            await standardError.WriteLineAsync("Server URL invalid or missing.");
            return null;
        }

        if (String.IsNullOrWhiteSpace(configuration.AdminToken))
        {
            await standardError.WriteLineAsync(MissingAdminTokenError);
            return null;
        }

        return new(serverUrl, configuration.AdminToken);
    }
}
