// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke tests for the queue-download flow: upload several files with the CLI, then download the
/// generated secret-free queue and verify every file is reproduced byte-for-byte at its intended destination.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class QueueDownloadSmokeTests : SmokeTestBase
{
    [Test]
    public async Task QueueDownload_ShouldReproduceFlattenedDestinations_WhenUploadedWithFlatten()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-queue-flat");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var nestedDirectory = workspace.CreateSubdirectory(Path.Combine("inputs", "nested"));
        FileInfo[] inputs =
        [
            CreateInputFile(inputDirectory, "alpha.bin", 1),
            CreateInputFile(nestedDirectory, "charlie.bin", 3)
        ];

        var queuePath = Path.Combine(workspace.Path, "flat.queue.json");

        var uploadArguments = new List<String>
        {
            "upload"
        };
        uploadArguments.AddRange(inputs.Select(static input => input.FullName));
        uploadArguments.AddRange([
            "--server-url", api.BaseAddress.AbsoluteUri,
            "--upload-token", api.AdminToken,
            "--queue-out", queuePath,
            "--flatten"
        ]);

        var upload = await CliRunner.RunAsync(Artifacts, uploadArguments, workspace.Path);
        upload.ExitCode.Should().Be(0, $"the upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareKey = RequireOutputValue(upload, "share-key:");
        var outputRoot = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(
            Artifacts,
            ["download", "--queue", queuePath, "--output-root", outputRoot, "--share-key", shareKey],
            workspace.Path);
        download.ExitCode.Should().Be(0, $"the queue download should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        foreach (var input in inputs)
        {
            AssertFilesEqual(input, Path.Combine(outputRoot, input.Name));
        }
    }

    [Test]
    public async Task QueueDownload_ShouldReproduceRecursivelySelectedFilteredFilesByteForByte()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-queue-recursive");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var nestedDirectory = workspace.CreateSubdirectory(Path.Combine("inputs", "nested"));
        var alpha = CreateInputFile(inputDirectory, "alpha.bin", 5);
        var bravo = CreateInputFile(nestedDirectory, "bravo.bin", 7);
        CreateInputFile(nestedDirectory, "ignored.bin", 9);
        CreateInputFile(inputDirectory, "notes.txt", 11);
        var queuePath = Path.Combine(workspace.Path, "recursive.queue.json");

        var upload = await CliRunner.RunAsync(
            Artifacts,
            [
                "upload", inputDirectory,
                "--recursive",
                "--include", "**/*.bin",
                "--exclude", "**/ignored.bin",
                "--server-url", api.BaseAddress.AbsoluteUri,
                "--upload-token", api.AdminToken,
                "--queue-out", queuePath
            ],
            workspace.Path);
        upload.ExitCode.Should().Be(0, $"the recursive upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareKey = RequireOutputValue(upload, "share-key:");
        var outputRoot = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(
            Artifacts,
            ["download", "--queue", queuePath, "--output-root", outputRoot, "--share-key", shareKey],
            workspace.Path);
        download.ExitCode.Should().Be(0, $"the queue download should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(alpha, Path.Combine(outputRoot, "inputs", "alpha.bin"));
        AssertFilesEqual(bravo, Path.Combine(outputRoot, "inputs", "nested", "bravo.bin"));
        File.Exists(Path.Combine(outputRoot, "inputs", "nested", "ignored.bin")).Should().BeFalse();
        File.Exists(Path.Combine(outputRoot, "inputs", "notes.txt")).Should().BeFalse();
    }

    // The default preserve mode makes destinations relative to the command's working directory, so a nested input
    // must be recreated at the same relative path under the download output root.
    [Test]
    public async Task QueueDownload_ShouldReproduceUploadRelativePathsByteForByte()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-queue");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var nestedDirectory = workspace.CreateSubdirectory(Path.Combine("inputs", "nested"));
        var inputs = new Dictionary<String, FileInfo>
        {
            ["inputs/alpha.bin"] = CreateInputFile(inputDirectory, "alpha.bin", 1),
            ["inputs/bravo.bin"] = CreateInputFile(inputDirectory, "bravo.bin", 2),
            ["inputs/nested/charlie.bin"] = CreateInputFile(nestedDirectory, "charlie.bin", 3)
        };

        var queuePath = Path.Combine(workspace.Path, "share.queue.json");

        var uploadArguments = new List<String>
        {
            "upload"
        };
        uploadArguments.AddRange(inputs.Values.Select(static input => input.FullName));
        uploadArguments.AddRange([
            "--server-url", api.BaseAddress.AbsoluteUri,
            "--upload-token", api.AdminToken,
            "--queue-out", queuePath
        ]);

        var upload = await CliRunner.RunAsync(Artifacts, uploadArguments, workspace.Path);
        upload.ExitCode.Should().Be(0, $"the upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareKey = RequireOutputValue(upload, "share-key:");
        File.Exists(queuePath).Should().BeTrue("the queue file should have been written.");

        var outputRoot = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(
            Artifacts,
            ["download", "--queue", queuePath, "--output-root", outputRoot, "--share-key", shareKey],
            workspace.Path);
        download.ExitCode.Should().Be(0, $"the queue download should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        foreach (var (relativePath, input) in inputs)
        {
            AssertFilesEqual(input, Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
