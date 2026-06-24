// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke test for the queue-download flow: upload several files with the CLI, then download
/// the generated secret-free queue and verify every file is reproduced byte-for-byte.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class QueueDownloadSmokeTests : SmokeTestBase
{
    [Test]
    public async Task QueueDownload_ShouldReproduceEveryUploadedFileByteForByte()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-queue");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        FileInfo[] inputs =
        [
            CreateInputFile(inputDirectory, "alpha.bin", 1),
            CreateInputFile(inputDirectory, "bravo.bin", 2),
            CreateInputFile(inputDirectory, "charlie.bin", 3)
        ];

        var queuePath = Path.Combine(workspace.Path, "share.queue.json");

        var uploadArguments = new List<String>
        {
            "upload"
        };
        uploadArguments.AddRange(inputs.Select(static input => input.FullName));
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

        foreach (var input in inputs)
        {
            AssertFilesEqual(input, Path.Combine(outputRoot, input.Name));
        }
    }
}
