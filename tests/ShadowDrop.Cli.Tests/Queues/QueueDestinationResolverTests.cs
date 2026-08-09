// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Queues;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Queues;

/// <summary>
/// The resolver decides destinations from path text alone and never touches the file system, so these tests use
/// fabricated absolute paths and run identically on every host.
/// </summary>
public sealed class QueueDestinationResolverTests
{
    private static readonly String InputRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "shadowdrop-destinations", "root"));
    private static readonly String UnrelatedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "shadowdrop-destinations", "elsewhere"));

    [Test]
    public void TryResolve_ShouldAcceptUnrelatedLocations_InFlattenMode()
    {
        var files = new[]
        {
            InputFile(InputRoot, "sub", "file3"),
            InputFile(UnrelatedRoot, "deep", "file4")
        };

        var succeeded = QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Flatten, InputRoot,
                                                            out var destinations, out var error);

        succeeded.Should().BeTrue();
        error.Should().BeNull();
        Paths(destinations, files).Should().Equal("file3", "file4");
    }

    [Test]
    public void TryResolve_ShouldCarryThePreCollisionLeafAsTheExpectedFileName()
    {
        var files = new[]
        {
            InputFile(InputRoot, "a", "report.txt"),
            InputFile(InputRoot, "b", "report.txt")
        };

        QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Flatten, InputRoot,
                                            out var destinations, out _)
                                .Should().BeTrue();

        Paths(destinations, files).Should().Equal("report.txt", "report (2).txt");

        // The manifest announces the uploaded name, which never carries the collision suffix.
        destinations.Should().NotBeNull();
        destinations.Values.Select(destination => destination.ExpectedFileName).Should().AllBe("report.txt");
    }

    [Test]
    public void TryResolve_ShouldPreserveRelativeDirectories()
    {
        var files = new[]
        {
            InputFile(InputRoot, "file1"),
            InputFile(InputRoot, "sub", "file3"),
            InputFile(InputRoot, "docs", "doc1")
        };

        var succeeded = QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                                            out var destinations, out var error);

        succeeded.Should().BeTrue();
        error.Should().BeNull();
        Paths(destinations, files).Should().Equal("file1", "sub/file3", "docs/doc1");
    }

    [Test]
    public void TryResolve_ShouldRejectAncestorDescendantConflict()
    {
        var files = new[]
        {
            InputFile(InputRoot, "docs"),
            InputFile(InputRoot, "docs", "report.txt")
        };

        var succeeded = QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                                            out var destinations, out var error);

        succeeded.Should().BeFalse();
        destinations.Should().BeNull();
        error.Should().Contain("'docs' is also used as a directory").And.Contain("--flatten");
    }

    [Test]
    public void TryResolve_ShouldRejectFileOutsideInputRoot()
    {
        var files = new[]
        {
            InputFile(InputRoot, "inside.bin"),
            InputFile(UnrelatedRoot, "outside.bin")
        };

        var succeeded = QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                                            out var destinations, out var error);

        succeeded.Should().BeFalse();
        destinations.Should().BeNull();
        error.Should().Contain("outside.bin")
             .And.Contain("--input-root")
             .And.Contain("--flatten");
    }

    [Test]
    public void TryResolve_ShouldReplaceTheLeafWithTheDisplayName_AndKeepTheDerivedDirectory()
    {
        var file = InputFile(InputRoot, "sub", "file3");
        var overrides = new Dictionary<String, String>
        {
            [file.FullName] = "renamed.txt"
        };

        var succeeded = QueueDestinationResolver.TryResolve([file], overrides, QueueDestinationMode.Preserve, InputRoot,
                                                            out var destinations, out var error);

        succeeded.Should().BeTrue();
        error.Should().BeNull();
        Paths(destinations, [file]).Should().Equal("sub/renamed.txt");
    }

    [Test]
    public void TryResolve_ShouldSanitizeEveryPathSegment()
    {
        var file = InputFile(InputRoot, "a>b", "c?d.txt");

        QueueDestinationResolver.TryResolve([file], NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                            out var destinations, out _)
                                .Should().BeTrue();

        Paths(destinations, [file]).Should().Equal("a_b/c_d.txt");
    }

    [Test]
    public void TryResolve_ShouldSuffixCaseOnlyDuplicatesWithinTheSameDirectory()
    {
        var files = new[]
        {
            InputFile(InputRoot, "sub", "Report.txt"),
            InputFile(InputRoot, "sub", "report.txt")
        };

        QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                            out var destinations, out _)
                                .Should().BeTrue();

        Paths(destinations, files).Should().Equal("sub/Report.txt", "sub/report (2).txt");
    }

    // Files in different source directories still collide once flattened, and the collision is resolved
    // deterministically rather than rejected.
    [Test]
    public void TryResolve_ShouldSuffixFlattenedCollisions()
    {
        var files = new[]
        {
            InputFile(InputRoot, "a", "report.txt"),
            InputFile(InputRoot, "b", "report.txt")
        };

        QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Flatten, InputRoot,
                                            out var destinations, out _)
                                .Should().BeTrue();

        Paths(destinations, files).Should().Equal("report.txt", "report (2).txt");
    }

    [Test]
    public void TryResolve_ShouldUseOperatingSystemPathComparisonForDuplicateSources()
    {
        var files = new[]
        {
            InputFile(InputRoot, "Report.txt"),
            InputFile(InputRoot, "report.txt")
        };

        var succeeded = QueueDestinationResolver.TryResolve(files, NoDisplayNames(), QueueDestinationMode.Preserve, InputRoot,
                                                            out var destinations, out var error);

        if (OperatingSystem.IsWindows())
        {
            succeeded.Should().BeFalse();
            destinations.Should().BeNull();
            error.Should().Contain("selected more than once");
        }
        else
        {
            succeeded.Should().BeTrue();
            error.Should().BeNull();
            Paths(destinations, files).Should().Equal("Report.txt", "report (2).txt");
        }
    }

    [Test]
    public void TryResolve_ShouldUseTheDisplayNameAsTheFlatLeaf()
    {
        var file = InputFile(UnrelatedRoot, "deep", "file4");
        var overrides = new Dictionary<String, String>
        {
            [file.FullName] = "renamed.txt"
        };

        QueueDestinationResolver.TryResolve([file], overrides, QueueDestinationMode.Flatten, InputRoot,
                                            out var destinations, out _)
                                .Should().BeTrue();

        Paths(destinations, [file]).Should().Equal("renamed.txt");
    }

    private static FileInfo InputFile(String root, params String[] segments) => new(Path.Combine([root, .. segments]));

    private static IReadOnlyDictionary<String, String> NoDisplayNames() => new Dictionary<String, String>();

    private static IEnumerable<String> Paths(IReadOnlyDictionary<String, QueueDestination>? destinations, IReadOnlyList<FileInfo> files)
    {
        destinations.Should().NotBeNull();
        return files.Select(file => destinations[file.FullName].Path);
    }
}
