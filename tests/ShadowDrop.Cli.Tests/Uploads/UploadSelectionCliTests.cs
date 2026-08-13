// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Tests.Fakes;

[NonParallelizable]
public sealed class UploadSelectionCliTests
{
    private String _rootDirectory;

    [Test]
    public async Task Help_ShouldDescribeRecursiveSelectionForBothUploadCommands()
    {
        var uploadOut = new StringWriter();
        var rawOut = new StringWriter();

        (await CliApplication.InvokeAsync(["upload", "--help"], CreateServices(uploadOut, new()), CancellationToken.None)).Should().Be(0);
        (await CliApplication.InvokeAsync(["upload", "raw", "--help"], CreateServices(rawOut, new()), CancellationToken.None)).Should().Be(0);

        foreach (var help in new[]
                 {
                     uploadOut.ToString(),
                     rawOut.ToString()
                 })
        {
            help.Should().Contain("input-paths")
                .And.Contain("-r, --recursive")
                .And.Contain("-i, --include")
                .And.Contain("-x, --exclude")
                .And.Contain("--files-from")
                .And.Contain("--dry-run")
                .And.Contain("directory-relative")
                .And.Contain("exclusion wins");
        }
    }

    [TestCase("upload")]
    [TestCase("raw")]
    public async Task InvokeAsync_ShouldAcceptAliasesAndRepeatedFilters(String command)
    {
        var root = CreateDirectory("tree");
        CreateFile("tree/keep.txt");
        CreateFile("tree/keep.pdf");
        CreateFile("tree/skip.pdf");
        var standardError = new StringWriter();
        var arguments = command == "raw"
            ? new[]
            {
                "upload",
                "raw",
                root,
                "-r",
                "-i",
                "**/*.txt",
                "-i",
                "**/*.pdf",
                "-x",
                "**/skip.pdf"
            }
            : new[]
            {
                "upload",
                root,
                "-r",
                "-i",
                "**/*.txt",
                "-i",
                "**/*.pdf",
                "-x",
                "**/skip.pdf"
            };

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(new(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Server URL invalid or missing.")
                     .And.NotContain("did not select any files")
                     .And.NotContain("requires --recursive");
    }

    [TestCase("upload")]
    [TestCase("raw")]
    public async Task InvokeAsync_ShouldAllowFilesFromWithoutPositionalOperands(String command)
    {
        var input = CreateFile("listed.bin");
        var listPath = Path.Combine(_rootDirectory, "inputs.txt");
        await File.WriteAllTextAsync(listPath, input + Environment.NewLine);
        var standardError = new StringWriter();
        var arguments = command == "raw"
            ? new[]
            {
                "upload",
                "raw",
                "--files-from",
                listPath
            }
            : new[]
            {
                "upload",
                "--files-from",
                listPath
            };

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(new(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Server URL invalid or missing.")
                     .And.NotContain("Required argument missing");
    }

    [Test]
    public async Task InvokeAsync_ShouldIdentifyInvalidListRecordSource()
    {
        var listPath = Path.Combine(_rootDirectory, "inputs.txt");
        await File.WriteAllTextAsync(listPath, "missing.bin\n");
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--files-from", listPath], CreateServices(new(), standardError),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("File is missing.")
                     .And.Contain(listPath)
                     .And.Contain("record 1");
    }

    [TestCase("upload")]
    [TestCase("raw")]
    public async Task InvokeAsync_ShouldNameExcludedFileLinksWhenTheySelectNothing(String command)
    {
        var root = CreateDirectory("tree");
        var outside = CreateFile("outside/secret.bin");
        var linkPath = Path.Combine(root, "linked.bin");
        File.CreateSymbolicLink(linkPath, outside);
        var standardError = new StringWriter();
        String[] arguments = command == "raw"
            ? ["upload", "raw", root, "--recursive"]
            : ["upload", root, "--recursive"];

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(new(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain(linkPath)
                     .And.Contain("will not be uploaded")
                     .And.Contain("No input files were selected.");
    }

    // A valueless repeatable option must not read as "no filter": that would silently upload a wider selection
    // than the invocation asks for.
    [TestCase("upload", "--include", "The --include option requires a glob pattern.")]
    [TestCase("upload", "--exclude", "The --exclude option requires a glob pattern.")]
    [TestCase("upload", "--files-from", "The --files-from option requires a file path or '-'.")]
    [TestCase("raw", "--include", "The --include option requires a glob pattern.")]
    [TestCase("raw", "--exclude", "The --exclude option requires a glob pattern.")]
    [TestCase("raw", "--files-from", "The --files-from option requires a file path or '-'.")]
    public async Task InvokeAsync_ShouldRejectRepeatableOptionsSuppliedWithoutAValue(String command, String option, String expectedError)
    {
        var root = CreateDirectory("tree");
        CreateFile("tree/keep.pdf");
        CreateFile("tree/notes.txt");
        var standardError = new StringWriter();
        String[] arguments = command == "raw"
            ? ["upload", "raw", root, "--recursive", option]
            : ["upload", root, "--recursive", option];

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(new(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain(expectedError)
                     .And.NotContain("Required argument missing");
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectStdinWithInteractiveBeforeReadingIt()
    {
        var standardError = new StringWriter();
        var reader = new ThrowingTextReader();
        var services = CreateServices(new(), standardError) with
        {
            StandardInput = reader
        };

        var exitCode = await CliApplication.InvokeAsync(["upload", "--interactive", "--files-from", "-"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--files-from - cannot be combined with --interactive");
        reader.WasRead.Should().BeFalse();
    }

    [TestCase("upload")]
    [TestCase("raw")]
    public async Task InvokeAsync_ShouldReportExcludedFileLinksWithoutFailingSelection(String command)
    {
        var root = CreateDirectory("tree");
        var visible = CreateFile("visible.bin");
        var outside = CreateFile("outside/secret.bin");
        var linkPath = Path.Combine(root, "linked.bin");
        File.CreateSymbolicLink(linkPath, outside);
        var standardError = new StringWriter();
        String[] arguments = command == "raw"
            ? ["upload", "raw", visible, root, "--recursive"]
            : ["upload", visible, root, "--recursive"];

        var exitCode = await CliApplication.InvokeAsync(arguments, CreateServices(new(), standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain(linkPath)
                     .And.Contain("will not be uploaded")
                     .And.Contain("Server URL invalid or missing.")
                     .And.NotContain("did not select any files");
    }

    [Test]
    public async Task InvokeAsync_ShouldResolveDisplayNameMappingsAgainstCapturedWorkingDirectory()
    {
        CreateFile(Path.Combine("nested", "mapped.bin"));
        var standardError = new StringWriter();
        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_rootDirectory);
        try
        {
            var invocation = CliApplication.InvokeAsync(
                ["upload", "nested", "--recursive", "--display-name", $"nested{Path.DirectorySeparatorChar}mapped.bin=Mapped.bin"],
                CreateServices(new(), standardError),
                CancellationToken.None);
            Directory.SetCurrentDirectory(previousDirectory);
            (await invocation).Should().Be(1);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        standardError.ToString().Should().Contain("Server URL invalid or missing.")
                     .And.NotContain("No file matches");
    }

    [Test]
    public async Task InvokeAsync_ShouldResolveStdinRecordsAgainstCapturedWorkingDirectory()
    {
        CreateFile("relative.bin");
        var standardError = new StringWriter();
        var services = CreateServices(new(), standardError) with
        {
            StandardInput = new StringReader("relative.bin\n")
        };
        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_rootDirectory);
        try
        {
            var invocation = CliApplication.InvokeAsync(["upload", "--files-from", "-"], services, CancellationToken.None);
            Directory.SetCurrentDirectory(previousDirectory);
            (await invocation).Should().Be(1);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }

        standardError.ToString().Should().Contain("Server URL invalid or missing.")
                     .And.NotContain("File is missing");
    }

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                      "artifacts",
                                      "upload-selection-cli-tests",
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

    private static CliApplicationServices CreateServices(StringWriter standardOut, StringWriter standardError) =>
        new(FakeConfiguration.Resolver(),
            _ => new(new NeverCalledHandler()),
            standardOut,
            standardError,
            new FakeInteractiveSession(),
            TimeProvider.System,
            new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
            FixedTerminalCapabilityProvider.Plain);

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

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }

    private sealed class ThrowingTextReader : TextReader
    {
        public Boolean WasRead { get; private set; }

        public override String ReadToEnd()
        {
            WasRead = true;
            throw new AssertionException("Standard input should not be read.");
        }
    }
}
