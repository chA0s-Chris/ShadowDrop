// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Updates;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Updates;
using ShadowDrop.Tests.Fakes;

public sealed class FileUpdateCheckCacheTests
{
    private String _cacheDirectory;

    [Test]
    public void Read_ShouldReturnNull_WhenCacheFileIsCorrupt()
    {
        var cache = CreateCache(out var cacheFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
        File.WriteAllText(cacheFilePath, "{ not json");

        cache.Read().Should().BeNull();
    }

    [Test]
    public void Read_ShouldReturnNull_WhenCacheFileIsMissing()
    {
        var cache = CreateCache(out _);

        cache.Read().Should().BeNull();
    }

    [Test]
    public void Read_ShouldReturnNull_WhenNoCachePathIsResolvable()
    {
        var cache = new FileUpdateCheckCache(new FixedCachePathResolver(null));

        cache.Read().Should().BeNull();
    }

    [SetUp]
    public void SetUp() => _cacheDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"update-cache-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, true);
        }
    }

    [Test]
    public void Write_ShouldCreateMissingDirectories_AndRoundTripTheRecord()
    {
        var cache = CreateCache(out var cacheFilePath);
        var record = new UpdateCheckRecord(DateTimeOffset.Parse("2026-07-12T08:30:00Z"), "1.4.2");

        cache.Write(record);

        File.Exists(cacheFilePath).Should().BeTrue();
        cache.Read().Should().Be(record);
    }

    [Test]
    public void Write_ShouldOverwriteThePreviousRecord()
    {
        var cache = CreateCache(out _);
        cache.Write(new(DateTimeOffset.Parse("2026-07-11T08:30:00Z"), "1.4.1"));
        var replacement = new UpdateCheckRecord(DateTimeOffset.Parse("2026-07-12T08:30:00Z"), null);

        cache.Write(replacement);

        cache.Read().Should().Be(replacement);
    }

    [Test]
    public void Write_ShouldSwallowFailures_WhenNoCachePathIsResolvable()
    {
        var cache = new FileUpdateCheckCache(new FixedCachePathResolver(null));

        var action = () => cache.Write(new(DateTimeOffset.UtcNow, "1.0.0"));

        action.Should().NotThrow();
    }

    private FileUpdateCheckCache CreateCache(out String cacheFilePath)
    {
        cacheFilePath = Path.Combine(_cacheDirectory, "shadowdrop", "update-check.json");
        return new(new FixedCachePathResolver(cacheFilePath));
    }

    private sealed class FixedCachePathResolver(String? cacheFilePath) : UpdateCheckCachePathResolver(new FakeEnvironmentReader())
    {
        public override String? GetCacheFilePath() => cacheFilePath;
    }
}
