// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Real end-to-end smoke test for the client-side TLS trust configuration. A self-signed HTTPS listener is stood
/// up independently of the API process; the CLI is pointed at it both with <c>--cacert</c> (the certificate is
/// trusted, so the handshake succeeds and the request reaches the server) and with default trust (the certificate
/// is rejected, so the CLI aborts during the TLS handshake).
/// </summary>
[Category("E2E")]
[NonParallelizable]
public sealed class SelfSignedTlsSmokeTests : SmokeTestBase
{
    // A well-formed 32-byte (64 hex character) share key so the download reaches the network call before failing.
    private const String ShareKey = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    [Test]
    public async Task Download_ShouldFailDuringHandshake_WhenCertificateIsNotTrusted()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-tls");
        var caCertPath = Path.Combine(workspace.Path, "self-signed-ca.pem");
        await using var listener = SelfSignedTlsListener.Start(caCertPath);

        var result = await CliRunner.RunAsync(
            Artifacts,
            ["download", "self-signed-token", "--server-url", listener.BaseAddress.AbsoluteUri, "--share-key", ShareKey],
            workspace.Path);

        result.ExitCode.Should().Be(1, $"the download should fail because the self-signed certificate is not trusted.{Environment.NewLine}{result.Describe()}");
        result.StandardError.Should().Contain("Server connection failed.");
        listener.ServedRequests.Should().Be(0, "the CLI should reject the untrusted certificate and never send its request.");
    }

    [Test]
    public async Task Download_ShouldReachSelfSignedServer_WhenCaCertIsTrusted()
    {
        using var workspace = TempWorkspace.Create("shadowdrop-e2e-tls");
        var caCertPath = Path.Combine(workspace.Path, "self-signed-ca.pem");
        await using var listener = SelfSignedTlsListener.Start(caCertPath);

        var result = await CliRunner.RunAsync(
            Artifacts,
            ["download", "self-signed-token", "--server-url", listener.BaseAddress.AbsoluteUri, "--share-key", ShareKey, "--cacert", caCertPath],
            workspace.Path);

        // The endpoint always answers 404, so the command still fails, but it must fail at the HTTP layer
        // (the share is unavailable) rather than at the TLS layer.
        result.ExitCode.Should().Be(1, $"the download should reach the server and fail at the HTTP layer.{Environment.NewLine}{result.Describe()}");
        result.StandardError.Should().Contain("Share unavailable or unauthorized.")
              .And.NotContain("Server connection failed.");
        listener.ServedRequests.Should()
                .BeGreaterThan(0, "the CLI should have completed the TLS handshake and sent its request against the trusted certificate.");
    }
}
