// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// A minimal, self-contained HTTPS endpoint backed by a generated self-signed certificate. It exists purely to
/// exercise the CLI's client-side TLS trust configuration (<c>--cacert</c> / default trust): it stands up a TLS
/// listener independent of the API process, accepts connections, and answers every request with <c>404</c> so the
/// CLI either reaches the server (handshake succeeds) or aborts during the handshake (untrusted certificate).
/// </summary>
internal sealed class SelfSignedTlsListener : IAsyncDisposable
{
    private static readonly Byte[] NotFoundResponse =
        "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
    private readonly Task _acceptLoop;
    private readonly CancellationTokenSource _cancellation = new();

    private readonly X509Certificate2 _certificate;
    private readonly TcpListener _listener;
    private Int32 _servedRequests;

    private SelfSignedTlsListener(TcpListener listener, X509Certificate2 certificate, Uri baseAddress)
    {
        _listener = listener;
        _certificate = certificate;
        BaseAddress = baseAddress;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The HTTPS base URL the listener is bound to, with a trailing slash.</summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// The number of requests that were fully received over a completed TLS session. A client that does not trust
    /// the certificate aborts after the handshake and never sends its request, so this stays at zero for it.
    /// </summary>
    public Int32 ServedRequests => Volatile.Read(ref _servedRequests);

    public static SelfSignedTlsListener Start(String certificatePemPath)
    {
        var certificate = CreateSelfSignedCertificate();
        File.WriteAllText(certificatePemPath, certificate.ExportCertificatePem());

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var baseAddress = new Uri($"https://127.0.0.1:{port}/");

        return new(listener, certificate, baseAddress);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ShadowDrop Self-Signed TLS", rsa,
                                             HashAlgorithmName.SHA256,
                                             RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeName.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeName.Build());

        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Round-trip through PKCS#12 so the private key is usable by SslStream on every platform.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (Exception) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = HandleConnectionAsync(client);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using (client)
        await using (var sslStream = new SslStream(client.GetStream(), false))
        {
            try
            {
                // A client that distrusts the certificate aborts the handshake here, so this throws for it.
                await sslStream.AuthenticateAsServerAsync(_certificate, false, false);

                // Read the request line. A trusting client sends its GET; an aborting client never gets this far.
                var buffer = new Byte[2048];
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var bytesRead = await sslStream.ReadAsync(buffer, readTimeout.Token);
                if (bytesRead <= 0)
                {
                    return;
                }

                Interlocked.Increment(ref _servedRequests);

                await sslStream.WriteAsync(NotFoundResponse, _cancellation.Token);
                await sslStream.FlushAsync(_cancellation.Token);
            }
            catch (Exception)
            {
                // A failed handshake (untrusted certificate) or a dropped connection is an expected outcome;
                // it simply does not count as a completed handshake.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();

        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // The accept loop is being torn down; any residual fault is irrelevant.
        }

        _cancellation.Dispose();
        _certificate.Dispose();
    }
}
