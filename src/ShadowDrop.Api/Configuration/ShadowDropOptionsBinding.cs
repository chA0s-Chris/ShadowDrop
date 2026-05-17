// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

using ShadowDrop.Api.Infrastructure.Storage;

public static class ShadowDropOptionsBinding
{
    public static ShadowDropOptions BindAndValidate(IConfiguration configuration, String contentRootPath)
    {
        var shadowDropSection = configuration.GetRequiredSection("ShadowDrop");
        var options = shadowDropSection.Get<ShadowDropOptions>()
                      ?? throw new InvalidOperationException("The 'ShadowDrop' configuration section is required.");

        if (String.IsNullOrWhiteSpace(options.Metadata.LiteDbPath))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Metadata:LiteDbPath' is required.");
        }

        if (String.IsNullOrWhiteSpace(options.Storage.LocalRoot))
        {
            throw new InvalidOperationException("The configuration value 'ShadowDrop:Storage:LocalRoot' is required.");
        }

        options.Metadata.LiteDbPath = ResolvePath(options.Metadata.LiteDbPath, contentRootPath);
        options.Storage.LocalRoot = ResolvePath(options.Storage.LocalRoot, contentRootPath);

        var metadataDirectory = Path.GetDirectoryName(options.Metadata.LiteDbPath)
                                ?? throw new InvalidOperationException("The metadata database path must include a directory.");
        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(metadataDirectory);
        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(options.Storage.LocalRoot);

        return options;
    }

    private static String ResolvePath(String configuredPath, String contentRootPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
}
