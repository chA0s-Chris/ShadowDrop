// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Tests.Fakes;

public sealed class UpdateCheckCachePathResolverTests
{
    [Test]
    public void GetCacheFilePath_ShouldFallBackToHomeCacheDirectory_WhenXdgCacheHomeIsUnset()
    {
        var resolver = new UpdateCheckCachePathResolver(new FakeEnvironmentReader(new Dictionary<String, String?>
        {
            ["HOME"] = "/home/alice"
        }), false);

        resolver.GetCacheFilePath().Should().Be(Path.Combine("/home/alice", ".cache", "shadowdrop", "update-check.json"));
    }

    [Test]
    public void GetCacheFilePath_ShouldPreferXdgCacheHome_OnUnix()
    {
        var resolver = new UpdateCheckCachePathResolver(new FakeEnvironmentReader(new Dictionary<String, String?>
        {
            ["XDG_CACHE_HOME"] = "/custom/cache",
            ["HOME"] = "/home/alice"
        }), false);

        resolver.GetCacheFilePath().Should().Be(Path.Combine("/custom/cache", "shadowdrop", "update-check.json"));
    }

    [Test]
    public void GetCacheFilePath_ShouldUseLocalApplicationData_OnWindows()
    {
        var resolver = new UpdateCheckCachePathResolver(new FakeEnvironmentReader(), true);

        var path = resolver.GetCacheFilePath();

        // The LocalApplicationData base folder is platform-provided; only the ShadowDrop-owned suffix is asserted.
        path.Should().NotBeNull().And.EndWith(Path.Combine("ShadowDrop", "Cache", "update-check.json"));
    }
}
