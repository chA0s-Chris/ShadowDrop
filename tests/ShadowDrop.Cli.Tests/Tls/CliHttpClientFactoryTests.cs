// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Tls;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Tls;
using System.Net.Security;

public sealed class CliHttpClientFactoryTests
{
    [Test]
    public void CreateClient_ShouldDisableOverallRequestTimeout()
    {
        using var client = CliHttpClientFactory.CreateClient(CliTlsOptions.Default);

        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldAcceptLeafChainingToTrustedRoot()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        using var leaf = TestCertificateAuthority.CreateLeafSignedBy(root);
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(root);

        callback(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldAcceptSelfSignedCertificateUsedAsItsOwnTrustAnchor()
    {
        using var selfSigned = TestCertificateAuthority.CreateSelfSignedRoot("CN=self-signed.test");
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(selfSigned);

        callback(selfSigned, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldAcceptWhenNoPolicyErrors()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        using var leaf = TestCertificateAuthority.CreateLeafSignedBy(root);
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(root);

        callback(leaf, null, SslPolicyErrors.None).Should().BeTrue();
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldRejectCertificateFromDifferentRoot()
    {
        using var trustedRoot = TestCertificateAuthority.CreateSelfSignedRoot("CN=trusted.test");
        using var otherRoot = TestCertificateAuthority.CreateSelfSignedRoot("CN=other.test");
        using var leaf = TestCertificateAuthority.CreateLeafSignedBy(otherRoot);
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(trustedRoot);

        callback(leaf, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldRejectHostnameMismatchEvenForTrustedRoot()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        using var leaf = TestCertificateAuthority.CreateLeafSignedBy(root);
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(root);

        callback(leaf, null, SslPolicyErrors.RemoteCertificateNameMismatch).Should().BeFalse();
    }

    [Test]
    public void CreateCustomTrustValidationCallback_ShouldRejectNullCertificate()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        var callback = CliHttpClientFactory.CreateCustomTrustValidationCallback(root);

        callback(null, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();
    }

    [Test]
    public void CreateHandler_ShouldAcceptAnyCertificate_WhenInsecure()
    {
        using var handler = (HttpClientHandler)CliHttpClientFactory.CreateHandler(new(null, true));
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();

        handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
        handler.ServerCertificateCustomValidationCallback!(new(), root, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
    }

    [Test]
    public void CreateHandler_ShouldNotOverrideValidation_WhenUsingSystemTrust()
    {
        using var handler = (HttpClientHandler)CliHttpClientFactory.CreateHandler(CliTlsOptions.Default);

        handler.ServerCertificateCustomValidationCallback.Should().BeNull();
    }

    [Test]
    public void CreateHandler_ShouldThrowConfigurationException_WhenCaCertFileIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"shadowdrop-missing-{Guid.NewGuid():N}.pem");

        var act = () => CliHttpClientFactory.CreateHandler(new(missingPath, false));

        act.Should().Throw<CliTlsConfigurationException>().WithMessage("*was not found*");
    }

    [Test]
    public void CreateHandler_ShouldThrowConfigurationException_WhenCaCertFileIsNotValidPem()
    {
        var invalidPath = WriteTempPem("this is not a certificate");

        try
        {
            var act = () => CliHttpClientFactory.CreateHandler(new(invalidPath, false));

            act.Should().Throw<CliTlsConfigurationException>().WithMessage("*not a valid PEM-encoded certificate*");
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Test]
    public void CreateHandler_ShouldTrustPemEncodedCaFile_WhenCaCertPathIsProvided()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        using var leaf = TestCertificateAuthority.CreateLeafSignedBy(root);
        var caCertPath = WriteTempPem(root.ExportCertificatePem());

        try
        {
            using var handler = (HttpClientHandler)CliHttpClientFactory.CreateHandler(new(caCertPath, false));

            handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();
            handler.ServerCertificateCustomValidationCallback!(new(), leaf, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeTrue();
        }
        finally
        {
            File.Delete(caCertPath);
        }
    }

    [Test]
    public void CreateInsecureValidationCallback_ShouldAcceptAnyCertificate()
    {
        using var root = TestCertificateAuthority.CreateSelfSignedRoot();
        var callback = CliHttpClientFactory.CreateInsecureValidationCallback();

        callback(root, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch).Should().BeTrue();
        callback(null, null, SslPolicyErrors.RemoteCertificateNotAvailable).Should().BeTrue();
    }

    private static String WriteTempPem(String content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shadowdrop-cacert-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, content);
        return path;
    }
}
