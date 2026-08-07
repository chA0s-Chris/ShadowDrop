// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Cli.Configuration;

internal sealed class FakeConfigPathResolver : CliConfigPathResolver
{
    private readonly String? _configFilePath;

    public FakeConfigPathResolver(String? configFilePath)
    {
        _configFilePath = configFilePath;
    }

    public override String? GetConfigFilePath() => _configFilePath;
}

internal sealed class FakeEnvironmentReader : IEnvironmentReader
{
    private readonly IReadOnlyDictionary<String, String?> _values;

    public FakeEnvironmentReader(IReadOnlyDictionary<String, String?> values)
    {
        _values = values;
    }

    public FakeEnvironmentReader()
        : this(new Dictionary<String, String?>()) { }

    public String? GetEnvironmentVariable(String variableName) => _values.TryGetValue(variableName, out var value) ? value : null;
}

internal static class FakeConfiguration
{
    public static CliConfigurationResolver Resolver(String? serverUrl = null, String? uploadToken = null, String? configFilePath = null,
                                                    String? adminToken = null)
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

        if (adminToken is not null)
        {
            values["SHADOWDROP_ADMIN_TOKEN"] = adminToken;
        }

        return new(new FakeConfigPathResolver(configFilePath), new FakeEnvironmentReader(values));
    }
}
