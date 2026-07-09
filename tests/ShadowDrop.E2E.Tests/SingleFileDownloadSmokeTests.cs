// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke test for the single-file download flow: upload one file with the CLI, then download the
/// share without <c>--out</c> and verify it lands at <c>./&lt;original-filename&gt;</c> byte-for-byte.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class SingleFileDownloadSmokeTests : SmokeTestBase
{
    [Test]
    public async Task SingleFileDownload_ShouldWriteToDefaultOutputPathInTheCurrentDirectory()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-single-file");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var input = CreateInputFile(inputDirectory, "report.bin", 11);

        var upload = await CliRunner.RunAsync(
            Artifacts,
            ["upload", input.FullName, "--server-url", api.BaseAddress.AbsoluteUri, "--upload-token", api.AdminToken],
            workspace.Path);
        upload.ExitCode.Should().Be(0, $"the upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareUrl = RequireOutputValue(upload, "share-url:");
        var shareKey = RequireOutputValue(upload, "share-key:");

        // No --out: the file must land in the download command's working directory under its original name.
        var downloadDirectory = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(Artifacts, ["download", shareUrl, "--share-key", shareKey], downloadDirectory);
        download.ExitCode.Should().Be(0, $"the single-file download should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(input, Path.Combine(downloadDirectory, input.Name));
    }
}
