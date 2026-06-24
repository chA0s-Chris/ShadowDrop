// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke test for the direct-HTTP flow: upload one file with <c>--direct-http</c> using
/// environment-variable configuration, then download the emitted <c>download-url:</c> with <c>curl</c> and
/// verify the bytes match. This exercises the server-side decryption path added in #55.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class DirectHttpDownloadSmokeTests : SmokeTestBase
{
    [Test]
    public async Task DirectHttpDownload_ShouldReproduceTheUploadedFileViaCurl()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-direct-http");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var input = CreateInputFile(inputDirectory, "payload.bin", 7);

        // Drive the server URL and upload token through environment variables so this scenario covers the
        // environment configuration path (the queue scenario covers the explicit-flag path).
        var environment = new Dictionary<String, String?>
        {
            ["SHADOWDROP_SERVER_URL"] = api.BaseAddress.AbsoluteUri,
            ["SHADOWDROP_UPLOAD_TOKEN"] = api.AdminToken
        };

        var upload = await CliRunner.RunAsync(Artifacts, ["upload", input.FullName, "--direct-http"], workspace.Path, environment);
        upload.ExitCode.Should().Be(0, $"the direct-HTTP upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var downloadUrl = RequireOutputValue(upload, "download-url:");

        var outputFile = Path.Combine(workspace.Path, "curl-output.bin");
        var curl = await CurlClient.DownloadAsync(downloadUrl, outputFile, workspace.Path, CancellationToken.None);
        curl.ExitCode.Should().Be(0, $"curl should download the direct-HTTP file.{Environment.NewLine}{curl.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(input, outputFile);
    }
}
