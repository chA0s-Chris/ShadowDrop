// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tls;

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Builds the shared <see cref="HttpClient"/> for a CLI invocation, applying the resolved <see cref="CliTlsOptions"/>
/// to the underlying message handler. The certificate-validation callbacks are exposed as static factory methods so
/// they can be unit-tested in isolation with locally generated certificate chains, without any real TLS I/O.
/// </summary>
internal static class CliHttpClientFactory
{
    /// <summary>Creates an <see cref="HttpClient"/> whose handler enforces the supplied TLS trust settings.</summary>
    /// <param name="options">The resolved TLS trust settings.</param>
    /// <returns>A new <see cref="HttpClient"/>; the caller owns its lifetime.</returns>
    /// <exception cref="CliTlsConfigurationException">The <c>--cacert</c> file is missing or not a valid PEM certificate.</exception>
    public static HttpClient CreateClient(CliTlsOptions options) => new(CreateHandler(options));

    /// <summary>
    /// Creates the validation callback used by <c>--cacert</c>. The presented chain is still validated, but
    /// <paramref name="trustedRoot"/> is added as a custom trust anchor so privately issued or self-signed
    /// certificates that chain up to it are accepted while genuinely untrusted certificates are rejected.
    /// </summary>
    /// <param name="trustedRoot">The additional root certificate to trust.</param>
    public static Func<X509Certificate2?, X509Chain?, SslPolicyErrors, Boolean> CreateCustomTrustValidationCallback(X509Certificate2 trustedRoot)
    {
        ArgumentNullException.ThrowIfNull(trustedRoot);

        return (certificate, chain, errors) =>
        {
            if (certificate is null)
            {
                return false;
            }

            // Already trusted by the system store: nothing more to prove.
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            // A hostname mismatch is never recoverable by trusting an extra root.
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                return false;
            }

            using var customChain = new X509Chain();
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.CustomTrustStore.Add(trustedRoot);

            if (chain is not null)
            {
                foreach (var element in chain.ChainElements)
                {
                    customChain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            return customChain.Build(certificate);
        };
    }

    /// <summary>Creates the configured <see cref="HttpMessageHandler"/> for the supplied TLS trust settings.</summary>
    /// <param name="options">The resolved TLS trust settings.</param>
    /// <returns>A handler that validates against the system trust, an additional custom root, or accepts all certificates.</returns>
    /// <exception cref="CliTlsConfigurationException">The <c>--cacert</c> file is missing or not a valid PEM certificate.</exception>
    public static HttpMessageHandler CreateHandler(CliTlsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Insecure)
        {
            var insecureCallback = CreateInsecureValidationCallback();
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) => insecureCallback(certificate, chain, errors)
            };
        }

        if (!String.IsNullOrWhiteSpace(options.CaCertPath))
        {
            var trustedRoot = LoadCertificate(options.CaCertPath);
            var trustCallback = CreateCustomTrustValidationCallback(trustedRoot);
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) => trustCallback(certificate, chain, errors)
            };
        }

        return new HttpClientHandler();
    }

    /// <summary>
    /// Creates the validation callback used by <c>--insecure</c>. It accepts every certificate unconditionally.
    /// </summary>
    public static Func<X509Certificate2?, X509Chain?, SslPolicyErrors, Boolean> CreateInsecureValidationCallback() =>
        static (_, _, _) => true;

    private static X509Certificate2 LoadCertificate(String path)
    {
        if (!File.Exists(path))
        {
            throw new CliTlsConfigurationException($"The certificate file specified by --cacert was not found: {path}");
        }

        String pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            throw new CliTlsConfigurationException($"The certificate file specified by --cacert could not be read: {path}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new CliTlsConfigurationException($"The certificate file specified by --cacert could not be read: {path}", exception);
        }

        try
        {
            return X509Certificate2.CreateFromPem(pem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new CliTlsConfigurationException(
                $"The certificate file specified by --cacert is not a valid PEM-encoded certificate: {path}", exception);
        }
    }
}
