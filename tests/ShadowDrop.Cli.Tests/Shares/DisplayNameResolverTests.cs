// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;

public sealed class DisplayNameResolverTests
{
    [Test]
    public void TryResolveForShareCreate_ShouldFail_OnDuplicateFileId()
    {
        var fileId = Guid.NewGuid();

        var resolved = DisplayNameResolver.TryResolveForShareCreate([fileId], [$"{fileId}=One.bin", $"{fileId}=Two.bin"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("Duplicate");
    }

    [Test]
    public void TryResolveForShareCreate_ShouldFail_OnNonGuidKey()
    {
        var fileId = Guid.NewGuid();

        var resolved = DisplayNameResolver.TryResolveForShareCreate([fileId], ["not-a-guid=Name.bin"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("not a valid file id");
    }

    [Test]
    public void TryResolveForShareCreate_ShouldFail_OnUnknownFileId()
    {
        var resolved = DisplayNameResolver.TryResolveForShareCreate([Guid.NewGuid()], [$"{Guid.NewGuid()}=Name.bin"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("No file id matches");
    }

    [Test]
    public void TryResolveForShareCreate_ShouldMapByFileId()
    {
        var fileId = Guid.NewGuid();

        var resolved = DisplayNameResolver.TryResolveForShareCreate([fileId], [$"{fileId}=Renamed.bin"], out var overrides, out var error);

        resolved.Should().BeTrue();
        error.Should().BeNull();
        overrides[fileId].Should().Be("Renamed.bin");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_OnDuplicateMappingKey()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file],
                                                               null,
                                                               [$"{file.FullName}=One.bin", $"{file.FullName}=Two.bin"],
                                                               out _,
                                                               out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("Duplicate");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_OnEmptyNormalizedMappingValue()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], null, [$"{file.FullName}=   "], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("is empty");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_OnMalformedMapping()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], null, ["no-separator"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("Invalid display-name mapping");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_OnUnknownMapping()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], null, ["does-not-exist.bin=Name.bin"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("No file matches");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_WhenNameCombinedWithDisplayName()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], "Name.bin", [$"{file.FullName}=Mapping.bin"], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("cannot be combined");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_WhenNameNormalizesToEmpty()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], "   ", [], out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("--name").And.Contain("empty");
    }

    [Test]
    public void TryResolveForUpload_ShouldFail_WhenNameUsedWithMultipleFiles()
    {
        var files = new[]
        {
            new FileInfo(Path.Combine(Path.GetTempPath(), "a.bin")),
            new FileInfo(Path.Combine(Path.GetTempPath(), "b.bin"))
        };

        var resolved = DisplayNameResolver.TryResolveForUpload(files, "Only One.bin", [], out var overrides, out var error);

        resolved.Should().BeFalse();
        overrides.Should().BeEmpty();
        error.Should().Contain("--name option requires exactly one file");
    }

    [Test]
    public void TryResolveForUpload_ShouldLeaveUnmappedFilesWithoutOverride()
    {
        var first = new FileInfo(Path.Combine(Path.GetTempPath(), "first.bin"));
        var second = new FileInfo(Path.Combine(Path.GetTempPath(), "second.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([first, second], null, [$"{first.FullName}=One.bin"], out var overrides, out _);

        resolved.Should().BeTrue();
        overrides.Should().ContainKey(first.FullName);
        overrides.Should().NotContainKey(second.FullName);
    }

    [Test]
    public void TryResolveForUpload_ShouldMapMultipleFiles_ByPath()
    {
        var first = new FileInfo(Path.Combine(Path.GetTempPath(), "first.bin"));
        var second = new FileInfo(Path.Combine(Path.GetTempPath(), "second.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([first, second],
                                                               null,
                                                               [$"{first.FullName}=One.bin", $"{second.FullName}=Two.bin"],
                                                               out var overrides,
                                                               out var error);

        resolved.Should().BeTrue();
        error.Should().BeNull();
        overrides[first.FullName].Should().Be("One.bin");
        overrides[second.FullName].Should().Be("Two.bin");
    }

    [Test]
    public void TryResolveForUpload_ShouldMapSingleFile_WhenNameProvided()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "report.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], "Quarterly Report.bin", [], out var overrides, out var error);

        resolved.Should().BeTrue();
        error.Should().BeNull();
        overrides.Should().ContainKey(file.FullName).WhoseValue.Should().Be("Quarterly Report.bin");
    }

    [Test]
    public void TryResolveForUpload_ShouldTrimName_ConsistentlyWithApi()
    {
        var file = new FileInfo(Path.Combine(Path.GetTempPath(), "report.bin"));

        var resolved = DisplayNameResolver.TryResolveForUpload([file], "  Trimmed.bin  ", [], out var overrides, out var error);

        resolved.Should().BeTrue();
        error.Should().BeNull();
        overrides[file.FullName].Should().Be("Trimmed.bin");
    }
}
