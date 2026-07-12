// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;

/// <summary>
/// Resolves the platform-conventional location of the update-check cache file:
/// <c>%LOCALAPPDATA%\ShadowDrop\Cache</c> on Windows and <c>$XDG_CACHE_HOME/shadowdrop</c> (falling back to
/// <c>~/.cache/shadowdrop</c>) on Linux and macOS.
/// </summary>
internal class UpdateCheckCachePathResolver(IEnvironmentReader environmentReader, Boolean isWindows)
{
    private const String CacheFileName = "update-check.json";

    public UpdateCheckCachePathResolver(IEnvironmentReader environmentReader)
        : this(environmentReader, OperatingSystem.IsWindows()) { }

    public virtual String? GetCacheFilePath()
    {
        if (isWindows)
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return String.IsNullOrWhiteSpace(localApplicationData)
                ? null
                : Path.Combine(localApplicationData, "ShadowDrop", "Cache", CacheFileName);
        }

        var cacheHome = environmentReader.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (String.IsNullOrWhiteSpace(cacheHome))
        {
            var homeDirectory = environmentReader.GetEnvironmentVariable("HOME");
            if (String.IsNullOrWhiteSpace(homeDirectory))
            {
                homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            if (String.IsNullOrWhiteSpace(homeDirectory))
            {
                return null;
            }

            cacheHome = Path.Combine(homeDirectory, ".cache");
        }

        return Path.Combine(cacheHome, "shadowdrop", CacheFileName);
    }
}
