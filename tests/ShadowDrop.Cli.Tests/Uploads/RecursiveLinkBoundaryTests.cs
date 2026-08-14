// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;

public sealed class RecursiveLinkBoundaryTests
{
    private String _rootDirectory;

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                      "artifacts",
                                      "recursive-link-boundary-tests",
                                      Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, true);
        }
    }

    [Test]
    public void TryResolveDirectoryRoot_ShouldResolveAnOperandReachedThroughLinkedAncestors()
    {
        var physical = CreateDirectory("real/tree");
        Directory.CreateSymbolicLink(Path.Combine(_rootDirectory, "mid"), Path.Combine(_rootDirectory, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_rootDirectory, "alias"), Path.Combine(_rootDirectory, "mid"));

        RecursiveLinkBoundary.TryResolveDirectoryRoot(Path.Combine(_rootDirectory, "alias", "tree"), out var root)
                             .Should().BeTrue();

        root.Should().Be(physical);
    }

    // 'top' resolves to a path that is itself reached through a link, so accepting this file requires
    // resolving the ancestry of a resolved target rather than only the segments that followed it.
    [Test]
    public void TryValidateFile_ShouldAcceptATargetWhoseResolvedPathHasLinkedAncestors()
    {
        var root = CreateDirectory("real/tree");
        CreateFile("real/tree/target.bin");
        Directory.CreateSymbolicLink(Path.Combine(_rootDirectory, "mid"), Path.Combine(_rootDirectory, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_rootDirectory, "top"), Path.Combine(_rootDirectory, "mid", "tree"));
        var link = Path.Combine(root, "linked.bin");
        File.CreateSymbolicLink(link, Path.Combine(_rootDirectory, "top", "target.bin"));

        RecursiveLinkBoundary.TryResolveDirectoryRoot(root, out var resolvedRoot).Should().BeTrue();
        RecursiveLinkBoundary.TryValidateFile(new(link), resolvedRoot).Should().BeTrue();
    }

    [Test]
    public void TryValidateFile_ShouldRejectACyclicLink()
    {
        var root = CreateDirectory("tree");
        var first = Path.Combine(root, "first.bin");
        var second = Path.Combine(root, "second.bin");
        File.CreateSymbolicLink(first, second);
        File.CreateSymbolicLink(second, first);

        RecursiveLinkBoundary.TryValidateFile(new(first), root).Should().BeFalse();
    }

    // The escape needs an existing file at the lexically collapsed path, because a resolver that folds
    // '..' as text lands there and would find the boundary satisfied by a file that is never read.
    [Test]
    public void TryValidateFile_ShouldRejectATargetReachedByDotDotThroughADirectoryLink()
    {
        var root = CreateDirectory("tree");
        CreateDirectory("outside");
        CreateFile("secret.bin");
        CreateFile("tree/secret.bin");
        Directory.CreateSymbolicLink(Path.Combine(root, "sub"), Path.Combine(_rootDirectory, "outside"));
        var link = Path.Combine(root, "linked.bin");
        File.CreateSymbolicLink(link, Path.Combine(root, "sub", "..", "secret.bin"));

        RecursiveLinkBoundary.TryValidateFile(new(link), root).Should().BeFalse();
    }

    [Test]
    public void TryValidateFile_ShouldTreatAFileAsInsideTheFilesystemRoot()
    {
        var target = CreateFile("root-boundary/target.bin");
        var link = Path.Combine(_rootDirectory, "root-boundary", "linked.bin");
        File.CreateSymbolicLink(link, target);
        var filesystemRoot = Path.GetPathRoot(link);

        filesystemRoot.Should().NotBeNullOrEmpty();
        RecursiveLinkBoundary.TryValidateFile(new(link), filesystemRoot).Should().BeTrue();
    }

    private String CreateDirectory(String relativePath)
    {
        var path = Path.Combine(_rootDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private String CreateFile(String relativePath)
    {
        var path = Path.Combine(_rootDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }
}
