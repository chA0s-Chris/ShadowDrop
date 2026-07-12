// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;

/// <summary>
/// Bundles the update feature's replaceable dependencies (network, filesystem cache, platform guidance, and
/// environment) so tests can swap them without ever making live release requests.
/// </summary>
internal sealed record CliUpdateServices(
    IUpdateReleaseClient ReleaseClient,
    IUpdateCheckCache Cache,
    IInstallationGuidanceProvider InstallationGuidance,
    IEnvironmentReader EnvironmentReader)
{
    public static CliUpdateServices CreateDefault()
    {
        var environmentReader = new EnvironmentReader();
        return new(new GitHubUpdateReleaseClient(),
                   new FileUpdateCheckCache(new(environmentReader)),
                   new InstallationGuidanceProvider(),
                   environmentReader);
    }
}
