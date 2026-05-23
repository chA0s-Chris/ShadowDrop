// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

using System.Text.Json;

internal sealed class CliConfigurationResolver(CliConfigPathResolver configPathResolver, IEnvironmentReader environmentReader)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        TypeInfoResolver = CliJsonSerializerContext.Default
    };

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

    private static String? FirstNonEmpty(params String?[] values) => values.FirstOrDefault(static value => !String.IsNullOrWhiteSpace(value));
}
