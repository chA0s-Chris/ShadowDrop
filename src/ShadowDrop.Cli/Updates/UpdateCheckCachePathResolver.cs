// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;

/// <summary>
/// Resolves the platform-conventional location of the update-check cache file:
/// <c>%LOCALAPPDATA%\ShadowDrop\Cache</c> on Windows and <c>$XDG_CACHE_HOME/shadowdrop</c> (falling back to
/// <c>~/.cache/shadowdrop</c>) on Linux and macOS.
/// </summary>
internal class UpdateCheckCachePathResolver
{
    private const String CacheFileName = "update-check.json";
    private readonly IEnvironmentReader _environmentReader;
    private readonly Boolean _isWindows;

    public UpdateCheckCachePathResolver(IEnvironmentReader environmentReader,
                                        Boolean isWindows)
    {
        _environmentReader = environmentReader;
        _isWindows = isWindows;
    }

    public UpdateCheckCachePathResolver(IEnvironmentReader environmentReader)
        : this(environmentReader, OperatingSystem.IsWindows()) { }

    public virtual String? GetCacheFilePath()
    {
        if (_isWindows)
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return String.IsNullOrWhiteSpace(localApplicationData)
                ? null
                : Path.Combine(localApplicationData, "ShadowDrop", "Cache", CacheFileName);
        }

        var cacheHome = _environmentReader.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (String.IsNullOrWhiteSpace(cacheHome))
        {
            var homeDirectory = _environmentReader.GetEnvironmentVariable("HOME");
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
