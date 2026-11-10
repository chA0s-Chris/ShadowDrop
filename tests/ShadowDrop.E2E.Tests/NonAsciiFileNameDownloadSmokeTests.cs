// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke tests for files whose names contain non-ASCII characters. Only a real Kestrel listener
/// rejects non-ASCII response header characters, so these scenarios guard the encoded file name headers (#252) in
/// both the CLI and the direct-HTTP download modes.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class NonAsciiFileNameDownloadSmokeTests : SmokeTestBase
{
    private const String NonAsciiFileName = "mañana-日本.bin";

    [Test]
    public async Task CliDownload_ShouldReproduceAFileWithANonAsciiName()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-non-ascii-cli");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var input = CreateInputFile(workspace.CreateSubdirectory("inputs"), NonAsciiFileName, 13);

        var upload = await CliRunner.RunAsync(
            Artifacts,
            ["upload", input.FullName, "--server-url", api.BaseAddress.AbsoluteUri, "--upload-token", api.AdminToken],
            workspace.Path);
        upload.ExitCode.Should().Be(0, $"the upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareUrl = RequireOutputValue(upload, "share-url:");
        var shareKey = RequireOutputValue(upload, "share-key:");

        var downloadDirectory = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(Artifacts, ["download", shareUrl, "--share-key", shareKey], downloadDirectory);
        download.ExitCode.Should().Be(0, $"the CLI download should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(input, Path.Combine(downloadDirectory, input.Name));
    }

    [Test]
    public async Task DirectHttpDownload_ShouldReproduceAFileWithANonAsciiNameViaCurl()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-non-ascii-direct-http");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var input = CreateInputFile(workspace.CreateSubdirectory("inputs"), NonAsciiFileName, 17);

        var upload = await CliRunner.RunAsync(
            Artifacts,
            ["upload", input.FullName, "--direct-http", "--server-url", api.BaseAddress.AbsoluteUri, "--upload-token", api.AdminToken],
            workspace.Path);
        upload.ExitCode.Should().Be(0, $"the direct-HTTP upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var downloadUrl = RequireOutputValue(upload, "download-url:");

        var outputFile = Path.Combine(workspace.Path, "curl-output.bin");
        var curl = await CurlClient.DownloadAsync(downloadUrl, outputFile, workspace.Path, CancellationToken.None);
        curl.ExitCode.Should().Be(0, $"curl should download the direct-HTTP file.{Environment.NewLine}{curl.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(input, outputFile);
    }
}
