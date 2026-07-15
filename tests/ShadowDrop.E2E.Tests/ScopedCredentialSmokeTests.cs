// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke test for the scoped upload-credential lifecycle: create a credential with the admin
/// token, upload with the scoped token, revoke the credential, verify the revoked token can no longer upload,
/// and verify the share created before revocation still downloads byte-for-byte.
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class ScopedCredentialSmokeTests : SmokeTestBase
{
    [Test]
    public async Task ScopedCredentialLifecycle_ShouldUploadShareAndBlockRevokedCredential()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-scoped-credential");
        var dataDirectory = workspace.CreateSubdirectory("api-data");

        await using var api = await ApiServerProcess.StartAsync(Artifacts, dataDirectory, CancellationToken.None);

        var create = await CliRunner.RunAsync(
            Artifacts,
            ["token", "create", "--name", "e2e-scoped", "--server-url", api.BaseAddress.AbsoluteUri, "--admin-token", api.AdminToken],
            workspace.Path);
        create.ExitCode.Should().Be(0, $"the credential creation should succeed.{Environment.NewLine}{create.Describe()}{api.DiagnosticsTail()}");

        var credentialId = RequireOutputValue(create, "credential-id:");
        var scopedToken = RequireOutputValue(create, "token:");
        scopedToken.Should().NotBe(api.AdminToken);

        var inputDirectory = workspace.CreateSubdirectory("inputs");
        var input = CreateInputFile(inputDirectory, "scoped.bin", 23);

        var upload = await CliRunner.RunAsync(
            Artifacts,
            ["upload", input.FullName, "--server-url", api.BaseAddress.AbsoluteUri, "--upload-token", scopedToken],
            workspace.Path);
        upload.ExitCode.Should().Be(0, $"the scoped upload should succeed.{Environment.NewLine}{upload.Describe()}{api.DiagnosticsTail()}");

        var shareUrl = RequireOutputValue(upload, "share-url:");
        var shareKey = RequireOutputValue(upload, "share-key:");

        var revoke = await CliRunner.RunAsync(
            Artifacts,
            ["token", "revoke", credentialId, "--server-url", api.BaseAddress.AbsoluteUri, "--admin-token", api.AdminToken],
            workspace.Path);
        revoke.ExitCode.Should().Be(0, $"the credential revocation should succeed.{Environment.NewLine}{revoke.Describe()}{api.DiagnosticsTail()}");
        RequireOutputValue(revoke, "token-revoked:").Should().Be(credentialId);

        var revokedUpload = await CliRunner.RunAsync(
            Artifacts,
            ["upload", input.FullName, "--server-url", api.BaseAddress.AbsoluteUri, "--upload-token", scopedToken],
            workspace.Path);
        revokedUpload.ExitCode.Should()
                     .NotBe(0, $"an upload with a revoked credential should fail.{Environment.NewLine}{revokedUpload.Describe()}{api.DiagnosticsTail()}");

        // Revocation blocks new operations only; the share created before it must remain downloadable.
        var downloadDirectory = workspace.CreateSubdirectory("downloads");
        var download = await CliRunner.RunAsync(Artifacts, ["download", shareUrl, "--share-key", shareKey], downloadDirectory);
        download.ExitCode.Should()
                .Be(0, $"the download of the pre-revocation share should succeed.{Environment.NewLine}{download.Describe()}{api.DiagnosticsTail()}");

        AssertFilesEqual(input, Path.Combine(downloadDirectory, input.Name));
    }
}
