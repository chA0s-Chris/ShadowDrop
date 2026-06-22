// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Cli.Configuration;

internal sealed class FakeConfigPathResolver(String? configFilePath) : CliConfigPathResolver
{
    public override String? GetConfigFilePath() => configFilePath;
}

internal sealed class FakeEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
{
    public FakeEnvironmentReader()
        : this(new Dictionary<String, String?>()) { }

    public String? GetEnvironmentVariable(String variableName) => values.TryGetValue(variableName, out var value) ? value : null;
}

internal static class FakeConfiguration
{
    public static CliConfigurationResolver Resolver(String? serverUrl = null, String? uploadToken = null, String? configFilePath = null)
    {
        var values = new Dictionary<String, String?>();
        if (serverUrl is not null)
        {
            values["SHADOWDROP_SERVER_URL"] = serverUrl;
        }

        if (uploadToken is not null)
        {
            values["SHADOWDROP_UPLOAD_TOKEN"] = uploadToken;
        }

        return new(new FakeConfigPathResolver(configFilePath), new FakeEnvironmentReader(values));
    }
}
