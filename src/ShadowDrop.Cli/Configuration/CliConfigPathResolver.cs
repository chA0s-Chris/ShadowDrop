// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

using ShadowDrop.Contracts;

internal class CliConfigPathResolver
{
    public virtual String? GetConfigFilePath()
    {
        var homeDirectory = Environment.GetEnvironmentVariable("HOME");
        if (String.IsNullOrWhiteSpace(homeDirectory))
        {
            homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (String.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        return Path.Combine(homeDirectory,
                            CliConfigPathConstants.ConfigDirectoryName,
                            CliConfigPathConstants.ApplicationDirectoryName,
                            CliConfigPathConstants.FileName);
    }
}
