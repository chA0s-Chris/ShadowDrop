// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

using ShadowDrop.Cli.Tls;
using System.Text.Json;

internal sealed class CliConfigurationResolver(CliConfigPathResolver configPathResolver, IEnvironmentReader environmentReader)
{
    public CliResolvedConfiguration Resolve(String? serverUrlOverride, String? uploadTokenOverride)
    {
        CliConfigFile? configFile = null;
        var configFilePath = configPathResolver.GetConfigFilePath();
        if (!String.IsNullOrWhiteSpace(configFilePath) && File.Exists(configFilePath))
        {
            using var stream = File.OpenRead(configFilePath);
            configFile = JsonSerializer.Deserialize(stream, CliJsonSerializerContext.Default.CliConfigFile);
        }

        var serverUrl = FirstNonEmpty(serverUrlOverride,
                                      environmentReader.GetEnvironmentVariable("SHADOWDROP_SERVER_URL"),
                                      configFile?.ServerUrl);
        var uploadToken = FirstNonEmpty(uploadTokenOverride,
                                        environmentReader.GetEnvironmentVariable("SHADOWDROP_UPLOAD_TOKEN"),
                                        configFile?.UploadToken)?.Trim();

        return new(serverUrl, uploadToken);
    }

    /// <summary>
    /// Resolves the effective TLS trust settings for the invocation. The <c>--cacert</c> path follows the
    /// flag → environment (<c>SHADOWDROP_CACERT</c>) precedence used for the other string settings. The
    /// <c>--insecure</c> toggle is <see langword="true"/> when the flag is present or <c>SHADOWDROP_INSECURE</c>
    /// is set to a truthy value (<c>1</c>, <c>true</c>, or <c>yes</c>, case-insensitive); there is intentionally
    /// no way to force it off via the flag once the environment variable is truthy.
    /// </summary>
    /// <param name="caCertOverride">The <c>--cacert</c> flag value, if supplied.</param>
    /// <param name="insecureFlag">Whether the <c>--insecure</c> flag was present.</param>
    /// <returns>The resolved TLS trust settings (config files are deliberately not consulted).</returns>
    public CliTlsOptions ResolveTls(String? caCertOverride, Boolean insecureFlag)
    {
        var caCertPath = FirstNonEmpty(caCertOverride, environmentReader.GetEnvironmentVariable("SHADOWDROP_CACERT"))?.Trim();
        var insecure = insecureFlag || EnvironmentValue.IsTruthy(environmentReader.GetEnvironmentVariable("SHADOWDROP_INSECURE"));

        return new(caCertPath, insecure);
    }

    private static String? FirstNonEmpty(params String?[] values) => values.FirstOrDefault(static value => !String.IsNullOrWhiteSpace(value));
}
