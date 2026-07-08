// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;

public sealed class DownloadDestinationResolverTests
{
    private const String FileId = "3f7c6d1a-6b2e-4c1a-9a5d-2c8e5f0b1d34";

    private readonly List<String> _scratchDirectories = [];

    [TearDown]
    public void DeleteScratchDirectories()
    {
        foreach (var directory in _scratchDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, true);
        }

        _scratchDirectories.Clear();
    }

    [Test]
    public void Resolve_ShouldCombineAnnouncedNameWithExistingDirectory_WhenOutNamesOne()
    {
        var directory = CreateScratchDirectory();

        DownloadDestinationResolver.Resolve(directory, File("report.pdf")).Should().Be(Path.Combine(directory, "report.pdf"));
    }

    [Test]
    public void Resolve_ShouldFallBackToFileId_WhenAnnouncedNameIsMissing()
    {
        DownloadDestinationResolver.ResolveAnnouncedFileName(File(null)).Should().Be(FileId);
        DownloadDestinationResolver.ResolveAnnouncedFileName(File("   ")).Should().Be(FileId);
    }

    [Test]
    public void Resolve_ShouldFallBackToFixedName_WhenNeitherNameNorFileIdIsUsable()
    {
        var file = new ShareManifestFileContract
        {
            FileId = null,
            FileName = null
        };

        DownloadDestinationResolver.ResolveAnnouncedFileName(file).Should().Be("download.bin");
    }

    [Test]
    public void Resolve_ShouldHonorAbsoluteAndRelativeOutPathsVerbatim()
    {
        var directory = CreateScratchDirectory();
        var absolutePath = Path.Combine(directory, "explicit-name.bin");

        DownloadDestinationResolver.Resolve(absolutePath, File("report.pdf")).Should().Be(absolutePath);

        // A '..' segment in the user's own --out is honored, unlike an announced name, which is reduced to a leaf.
        var traversingPath = Path.Combine(directory, "nested", "..", "sibling.bin");
        DownloadDestinationResolver.Resolve(traversingPath, File("report.pdf")).Should().Be(Path.Combine(directory, "sibling.bin"));
    }

    [Test]
    public void Resolve_ShouldReduceAnnouncedNameToItsLeaf_WhenItContainsSeparators()
    {
        var directory = CreateScratchDirectory();

        DownloadDestinationResolver.Resolve(directory, File("../../etc/passwd")).Should().Be(Path.Combine(directory, "passwd"));
        DownloadDestinationResolver.Resolve(directory, File("..\\..\\windows\\system32\\evil.dll")).Should().Be(Path.Combine(directory, "evil.dll"));
    }

    [TestCase(".")]
    [TestCase("..")]
    [TestCase("nested/")]
    public void Resolve_ShouldThrow_WhenAnnouncedNameCannotBeSanitized(String fileName)
    {
        var act = () => DownloadDestinationResolver.Resolve(CreateScratchDirectory(), File(fileName));

        act.Should().Throw<DownloadCommandException>().WithMessage("The shared file name cannot be used as a safe output file name.*");
    }

    [Test]
    public void Resolve_ShouldTreatOutAsDirectory_WhenItEndsWithASeparatorAndDoesNotExist()
    {
        var missingDirectory = Path.Combine(CreateScratchDirectory(), "missing");
        var outOption = missingDirectory + Path.DirectorySeparatorChar;

        DownloadDestinationResolver.Resolve(outOption, File("report.pdf")).Should().Be(Path.Combine(missingDirectory, "report.pdf"));
    }

    [Test]
    public void Resolve_ShouldTreatOutAsFilePath_WhenItNeitherEndsWithASeparatorNorNamesAnExistingDirectory()
    {
        var path = Path.Combine(CreateScratchDirectory(), "missing", "chosen-name.bin");

        DownloadDestinationResolver.Resolve(path, File("report.pdf")).Should().Be(path);
    }

    [Test]
    public void Resolve_ShouldUseCurrentDirectoryAndAnnouncedName_WhenOutIsAbsent()
    {
        var expected = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), "report.pdf");

        DownloadDestinationResolver.Resolve(null, File("report.pdf")).Should().Be(expected);
        DownloadDestinationResolver.Resolve("  ", File("report.pdf")).Should().Be(expected);
    }

    private static ShareManifestFileContract File(String? fileName) =>
        new()
        {
            FileId = FileId,
            FileName = fileName
        };

    private String CreateScratchDirectory()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "download-destination-resolver-tests",
                                     Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _scratchDirectories.Add(directory);
        return directory;
    }
}
