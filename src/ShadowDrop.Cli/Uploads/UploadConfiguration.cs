// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

/// <summary>
/// The resolved server URL and upload token used by the upload and share-creation command surfaces.
/// </summary>
internal readonly record struct ResolvedUploadConfiguration(Uri ServerUrl, String UploadToken);

/// <summary>
/// Resolves and validates the server URL and upload token shared by <c>upload</c>, <c>upload raw</c>, and
/// <c>share create</c>, emitting the generic error and returning <see langword="null"/> on failure.
/// </summary>
internal static class UploadConfiguration
{
    public static async Task<ResolvedUploadConfiguration?> ResolveAsync(CliConfigurationResolver configurationResolver,
                                                                        String? serverUrlOverride,
                                                                        String? uploadTokenOverride,
                                                                        TextWriter standardError)
    {
        CliResolvedConfiguration configuration;
        try
        {
            configuration = configurationResolver.Resolve(serverUrlOverride, uploadTokenOverride);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return null;
        }

        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || ((serverUrl.Scheme != Uri.UriSchemeHttp) && (serverUrl.Scheme != Uri.UriSchemeHttps)))
        {
            await standardError.WriteLineAsync("Server URL invalid or missing.");
            return null;
        }

        if (String.IsNullOrWhiteSpace(configuration.UploadToken))
        {
            await standardError.WriteLineAsync("Authentication token invalid or missing.");
            return null;
        }

        return new(serverUrl, configuration.UploadToken);
    }
}
