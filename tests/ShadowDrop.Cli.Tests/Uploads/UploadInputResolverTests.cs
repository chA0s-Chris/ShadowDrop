// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;
using System.Runtime.Versioning;
using System.Text;

public sealed class UploadInputResolverTests
{
    private String _rootDirectory;

    [Test]
    public void Resolve_ShouldExcludeOutOfTreeAndDanglingFileLinksWithDiagnostics()
    {
        var root = CreateDirectory("tree");
        var visible = CreateFile("visible.bin");
        var outside = CreateFile("outside/secret.bin");
        File.CreateSymbolicLink(Path.Combine(root, "outside.bin"), outside);
        File.CreateSymbolicLink(Path.Combine(root, "dangling.bin"), Path.Combine(_rootDirectory, "missing.bin"));

        var result = UploadInputResolver.Resolve([], [visible, root], true, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Select(static selection => selection.File.Name).Should().Equal("visible.bin");
        result.ExcludedFileCount.Should().Be(2);
        result.Diagnostics.Select(static diagnostic => diagnostic.Message).Should().HaveCount(2)
              .And.OnlyContain(static message => message.Contains("will not be uploaded", StringComparison.Ordinal));
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void Resolve_ShouldFailWhenDirectoryCannotBeEnumerated()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("Directory permission behavior is asserted on Linux.");
        }

        var root = CreateDirectory("unreadable");
        CreateFile("unreadable/file.bin");
        File.SetUnixFileMode(root, UnixFileMode.UserWrite);
        try
        {
            var result = UploadInputResolver.Resolve([], [root], true, [], [], [], _rootDirectory, new StringReader(""));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle()
                  .Which.Message.Should().Contain("could not be enumerated");
        }
        finally
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    public void Resolve_ShouldIncludeARelativeInTreeFileLinkThroughASymlinkedAncestor()
    {
        var physicalRoot = CreateDirectory("physical/tree");
        CreateFile("physical/tree/target.bin");
        var link = Path.Combine(physicalRoot, "linked.bin");
        File.CreateSymbolicLink(link, "target.bin");
        var operandParent = Path.Combine(_rootDirectory, "alias");
        Directory.CreateSymbolicLink(operandParent, Path.Combine(_rootDirectory, "physical"));

        var result = UploadInputResolver.Resolve([], [Path.Combine(operandParent, "tree")], true, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Select(static selection => selection.DirectoryRelativePath).Should().Equal("linked.bin", "target.bin");
        result.Diagnostics.Should().BeEmpty();
    }

    // The selection is invalid because nothing survived, but the excluded link is the reason for that and
    // must still be reported rather than replaced by an unexplained empty selection.
    [Test]
    public void Resolve_ShouldKeepDiagnosticsWhenExcludedLinksAreTheOnlyCandidates()
    {
        var root = CreateDirectory("tree");
        var outside = CreateFile("outside/secret.bin");
        var link = Path.Combine(root, "outside.bin");
        File.CreateSymbolicLink(link, outside);

        var result = UploadInputResolver.Resolve([], [root], true, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeFalse();
        result.Selections.Should().BeEmpty();
        result.ExcludedFileCount.Should().Be(1);
        result.Diagnostics.Should().ContainSingle()
              .Which.Message.Should().Contain(link).And.Contain("will not be uploaded");
        result.Errors.Should().ContainSingle()
              .Which.Message.Should().Contain("No input files were selected.");
    }

    // The pre-encryption boundary check is skipped when a selection carries no root, so an ordinary file must
    // carry one too. Without this, dropping the root anywhere between here and EncryptedFileContent would
    // disable that check silently and leave every other test green.
    [Test]
    public void Resolve_ShouldKeepTheResolvedRootOnEveryRecursiveSelection()
    {
        var physicalRoot = CreateDirectory("physical/tree");
        CreateFile("physical/tree/ordinary.bin");
        File.CreateSymbolicLink(Path.Combine(physicalRoot, "linked.bin"), "ordinary.bin");
        var operandParent = Path.Combine(_rootDirectory, "alias");
        Directory.CreateSymbolicLink(operandParent, Path.Combine(_rootDirectory, "physical"));
        var explicitFile = CreateFile("explicit.bin");

        var result = UploadInputResolver.Resolve([], [explicitFile, Path.Combine(operandParent, "tree")], true, [], [], [], _rootDirectory,
                                                 new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Where(static selection => selection.DirectoryRelativePath is not null)
              .Should().HaveCount(2)
              .And.OnlyContain(selection => selection.RecursiveRootPath == physicalRoot);
        result.Selections.Should().ContainSingle(static selection => selection.DirectoryRelativePath == null)
              .Which.RecursiveRootPath.Should().BeNull();
    }

    [Test]
    public void Resolve_ShouldNotApplyFiltersToExplicitFiles()
    {
        var explicitFile = CreateFile("explicit.txt");
        var root = CreateDirectory("tree");
        CreateFile("tree/selected.pdf");

        var result = UploadInputResolver.Resolve([], [explicitFile, root], true, ["*.pdf"], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Select(static selection => selection.File.Name).Should().Equal("explicit.txt", "selected.pdf");
    }

    [Test]
    public void Resolve_ShouldOrderEachRootAndApplyIncludeExcludePrecedence()
    {
        var firstRoot = CreateDirectory("first");
        CreateFile("first/zeta.pdf");
        CreateFile("first/docs/alpha.pdf");
        CreateFile("first/docs/skip.pdf");
        CreateFile("first/.hidden.pdf");
        CreateFile("first/notes.txt");
        var secondRoot = CreateDirectory("second");
        CreateFile("second/beta.pdf");

        var result = UploadInputResolver.Resolve([], [firstRoot, secondRoot], true, ["**/*.pdf"], ["**/skip.pdf"], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Select(static selection => selection.DirectoryRelativePath).Should().Equal(
            ".hidden.pdf",
            "docs/alpha.pdf",
            "zeta.pdf",
            "beta.pdf");
        result.ExcludedFileCount.Should().Be(2);
    }

    [Test]
    public void Resolve_ShouldPreserveSourceAndRecordOrderingAcrossInputListsAndStdin()
    {
        var positional = CreateFile("positional.bin");
        var listedOne = CreateFile("listed one.bin");
        var listedTwo = CreateFile("日本語.bin");
        var stdinFile = CreateFile(" stdin whitespace ");
        var listPath = Path.Combine(_rootDirectory, "inputs.txt");
        File.WriteAllText(listPath, $"{listedOne}\n\n{listedTwo}\n", new UTF8Encoding(false, true));

        var result = UploadInputResolver.Resolve([], [positional], false, [], [], [listPath, "-"], _rootDirectory,
                                                 new StringReader($"{stdinFile}\n"));

        result.IsValid.Should().BeTrue();
        result.Selections.Select(static selection => selection.File.FullName).Should().Equal(positional, listedOne, listedTwo, stdinFile);
        result.Selections.Select(static selection => selection.Origin.Source).Should().Equal("commandLine", listPath, listPath, "stdin");
        result.Selections.Select(static selection => selection.Origin.RecordNumber).Should().Equal(null, 1, 3, 1);
    }

    // A link operand resolves to a directory, so it reaches traversal and must be refused there rather than
    // silently expanded into whatever the link points at.
    [Test]
    public void Resolve_ShouldRefuseADirectoryOperandThatIsALink()
    {
        var target = CreateDirectory("target");
        CreateFile("target/secret.bin");
        var linkPath = Path.Combine(_rootDirectory, "linked");
        Directory.CreateSymbolicLink(linkPath, target);

        var result = UploadInputResolver.Resolve([], [linkPath], true, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeFalse();
        result.Selections.Should().BeEmpty();
        result.Errors.Should().ContainSingle()
              .Which.Message.Should().Contain(linkPath).And.Contain("is a link and will not be traversed");
    }

    [Test]
    public void Resolve_ShouldRejectMalformedUtf8AndRepeatedStdin()
    {
        var listPath = Path.Combine(_rootDirectory, "invalid.txt");
        File.WriteAllBytes(listPath, [0xC3, 0x28]);

        var malformed = UploadInputResolver.Resolve([], [], false, [], [], [listPath], _rootDirectory, new StringReader(""));
        var repeatedStdin = UploadInputResolver.Resolve([], [], false, [], [], ["-", "-"], _rootDirectory, new StringReader(""));

        malformed.Errors.Should().ContainSingle()
                 .Which.Message.Should().Contain("valid UTF-8");
        malformed.Errors[0].Origin.Source.Should().Be(listPath);
        repeatedStdin.Errors.Should().ContainSingle()
                     .Which.Message.Should().Contain("only once");
    }

    [Test]
    public void Resolve_ShouldRequireRecursiveForDirectories()
    {
        var root = CreateDirectory("tree");

        var result = UploadInputResolver.Resolve([], [root], false, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
              .Which.Message.Should().Contain(root).And.Contain("--recursive");
    }

    [Test]
    public void Resolve_ShouldSkipDirectoryLinksWithoutCountingThemAsExcluded()
    {
        var root = CreateDirectory("tree");
        var target = CreateDirectory("target");
        CreateFile("target/secret.bin");
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), target);
        CreateFile("tree/visible.bin");

        var result = UploadInputResolver.Resolve([], [root], true, [], [], [], _rootDirectory, new StringReader(""));

        result.IsValid.Should().BeTrue();
        result.Selections.Should().ContainSingle()
              .Which.File.Name.Should().Be("visible.bin");
        result.ExcludedFileCount.Should().Be(0);
    }

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                      "artifacts",
                                      "upload-input-resolver-tests",
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

    [TestCase("**/*.pdf", "report.pdf")]
    [TestCase("**/*.pdf", "docs/report.pdf")]
    [TestCase("docs/?eport.*", "docs/report.pdf")]
    [TestCase(@"literal/\*.txt", "literal/*.txt")]
    [TestCase(".*", ".secret")]
    public void UploadGlob_ShouldMatchDocumentedSemantics(String pattern, String path)
    {
        UploadGlob.TryCreate(pattern, out var glob, out var error).Should().BeTrue(error);

        glob.Should().NotBeNull();
        glob.IsMatch(path).Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("docs/**.pdf")]
    [TestCase("docs/***/file")]
    [TestCase(@"docs/\a")]
    [TestCase("docs/")]
    public void UploadGlob_ShouldRejectInvalidPatterns(String pattern)
    {
        UploadGlob.TryCreate(pattern, out var glob, out var error).Should().BeFalse();

        glob.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
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
