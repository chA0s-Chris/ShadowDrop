// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ShadowDrop.Api.CompositionRoot;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// The environment-variable test mutates process-wide state and every test binds a real listener, so the
// fixture opts out of parallel execution the same way the other host-booting fixtures do.
[TestFixture]
[NonParallelizable]
public sealed class KestrelHttpsConfigurationTests
{
    private const String CertificatePasswordVariable = "ASPNETCORE_Kestrel__Certificates__Default__Password";

    private const String CertificatePathVariable = "ASPNETCORE_Kestrel__Certificates__Default__Path";

    private const String HttpsPortsVariable = "ASPNETCORE_HTTPS_PORTS";

    [Test]
    public async Task ConfiguredPfxCertificate_ShouldServeARealTlsRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const String password = "test-pfx-password";
            using var certificate = CreateSelfSignedCertificate();
            var certificatePath = Path.Combine(root, "server.pfx");
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, password));

            await using var host = CreateHost(root, new Dictionary<String, String?>
            {
                ["HTTPS_PORTS"] = "0",
                ["Kestrel:Certificates:Default:Path"] = certificatePath,
                ["Kestrel:Certificates:Default:Password"] = password
            });
            await host.App.StartAsync();

            var response = await GetHealthLiveAsync(host.App, certificate);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task DocumentedEnvironmentVariables_ShouldServeARealTlsRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const String password = "test-environment-password";
            using var certificate = CreateSelfSignedCertificate();
            var certificatePath = Path.Combine(root, "server.pfx");
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, password));

            // Covers the environment-variable spelling documented in docs/DEPLOYMENT.md; the prefixed provider
            // has to map it onto the same Kestrel configuration keys the other tests set in memory.
            using var environment = new EnvironmentVariableScope(new Dictionary<String, String?>
            {
                [HttpsPortsVariable] = "0",
                [CertificatePathVariable] = certificatePath,
                [CertificatePasswordVariable] = password
            });

            await using var host = CreateHost(root, new Dictionary<String, String?>());
            await host.App.StartAsync();

            var response = await GetHealthLiveAsync(host.App, certificate);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task EncryptedPemCertificate_ShouldNotExposePasswordOrPrivateKeyInStartupLogs()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const String password = "DO-NOT-LOG-CERTIFICATE-PASSWORD";
            var certificatePath = Path.Combine(root, "server.pem");
            var privateKeyPath = Path.Combine(root, "server-key.pem");
            using var rsa = RSA.Create(2048);
            using var certificate = CreateSelfSignedCertificate(rsa);
            await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
            var privateKeyPem = rsa.ExportEncryptedPkcs8PrivateKeyPem(password,
                                                                      new(PbeEncryptionAlgorithm.Aes256Cbc,
                                                                          HashAlgorithmName.SHA256,
                                                                          100_000));
            await File.WriteAllTextAsync(privateKeyPath, privateKeyPem);

            await using var host = CreateHost(root, new Dictionary<String, String?>
            {
                ["HTTPS_PORTS"] = "0",
                ["Kestrel:Certificates:Default:Path"] = certificatePath,
                ["Kestrel:Certificates:Default:KeyPath"] = privateKeyPath,
                ["Kestrel:Certificates:Default:Password"] = password
            });
            await host.App.PrepareStartupAsync(host.Logger, CancellationToken.None);
            await host.App.StartAsync();

            var renderedLogs = host.Sink.Render();

            // Anchor the negative assertions below: without a captured startup log they would pass vacuously.
            renderedLogs.Should().NotBeEmpty().And.Contain("Effective configuration").And.Contain("Now listening on");
            renderedLogs.Should().NotContain(password);
            foreach (var privateKeyLine in privateKeyPem.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                                        .Where(static line => !line.StartsWith("-----", StringComparison.Ordinal)))
            {
                renderedLogs.Should().NotContain(privateKeyLine);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task HttpsEndpoint_ShouldRejectMissingCertificateFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await AssertStartupFailsAsync<FileNotFoundException>(root,
                                                                 new Dictionary<String, String?>
                                                                 {
                                                                     ["HTTPS_PORTS"] = "0",
                                                                     ["Kestrel:Certificates:Default:Path"] =
                                                                         Path.Combine(root, "missing.pfx")
                                                                 },
                                                                 "missing.pfx");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task HttpsEndpoint_ShouldRejectWrongCertificatePassword()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var certificate = CreateSelfSignedCertificate();
            var certificatePath = Path.Combine(root, "server.pfx");
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, "correct-password"));

            await AssertStartupFailsAsync<CryptographicException>(root,
                                                                  new Dictionary<String, String?>
                                                                  {
                                                                      ["HTTPS_PORTS"] = "0",
                                                                      ["Kestrel:Certificates:Default:Path"] = certificatePath,
                                                                      ["Kestrel:Certificates:Default:Password"] = "wrong-password"
                                                                  },
                                                                  "password");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task HttpsEndpoint_ShouldRejectPemCertificateWithoutPrivateKey()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var certificate = CreateSelfSignedCertificate();
            var certificatePath = Path.Combine(root, "server.pem");
            await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());

            await AssertStartupFailsAsync<NotSupportedException>(root,
                                                                 new Dictionary<String, String?>
                                                                 {
                                                                     ["HTTPS_PORTS"] = "0",
                                                                     ["Kestrel:Certificates:Default:Path"] = certificatePath
                                                                 },
                                                                 "private key");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task HttpsEndpoint_ShouldRejectMismatchedPemCertificateAndPrivateKey()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var certificate = CreateSelfSignedCertificate();
            using var unrelatedKey = RSA.Create(2048);
            var certificatePath = Path.Combine(root, "server.pem");
            var privateKeyPath = Path.Combine(root, "server-key.pem");
            await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
            await File.WriteAllTextAsync(privateKeyPath, unrelatedKey.ExportPkcs8PrivateKeyPem());

            await AssertStartupFailsAsync<InvalidOperationException>(root,
                                                                     new Dictionary<String, String?>
                                                                     {
                                                                         ["HTTPS_PORTS"] = "0",
                                                                         ["Kestrel:Certificates:Default:Path"] = certificatePath,
                                                                         ["Kestrel:Certificates:Default:KeyPath"] = privateKeyPath
                                                                     },
                                                                     "does not match");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task HttpEndpoint_ShouldRemainAvailableWithoutHttpsConfiguration()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await using var host = CreateHost(root, new Dictionary<String, String?>
            {
                ["HTTP_PORTS"] = "0"
            });
            await host.App.StartAsync();

            var response = await GetHealthLiveAsync(host.App);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            host.App.Urls.Should().OnlyContain(static address => address.StartsWith("http://", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Boots the real application host so the tests exercise ShadowDrop's own composition root, endpoints, and
    /// Serilog pipeline instead of a stand-in web application.
    /// </summary>
    private static TestHost CreateHost(String contentRoot, IReadOnlyDictionary<String, String?> values)
    {
        var sink = new RecordingSink();
        var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();

        // Pinning the content root and environment keeps the API's own appsettings files and the test host's
        // working directory out of the configuration. URLS is neutralized as well because an ambient value
        // would override the listeners each test configures explicitly.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<String, String?>
        {
            ["URLS"] = String.Empty,
            ["ShadowDrop:Metadata:Provider"] = "LiteDb",
            ["ShadowDrop:Metadata:LiteDbPath"] = Path.Combine(contentRoot, "metadata", "shadowdrop.db"),
            ["ShadowDrop:Storage:Provider"] = "FileSystem",
            ["ShadowDrop:Storage:LocalRoot"] = Path.Combine(contentRoot, "storage"),
            // The health endpoints stay mapped regardless of these toggles, so the TLS assertions need no API surface.
            ["ShadowDrop:ApiExposure:EnableAdminOperations"] = "false",
            ["ShadowDrop:ApiExposure:EnableUploads"] = "false",
            ["ShadowDrop:ApiExposure:EnablePublicDownloads"] = "false"
        });
        builder.Configuration.AddInMemoryCollection(values);

        // ConfigureDefaultLogging resolves sinks from the container, so the framework's own startup logging
        // lands in the same recording sink as the composition root's.
        builder.Services.AddSingleton<ILogEventSink>(sink);

        var app = builder.ConfigureServices(logger).Build().ConfigureMiddleware(logger);
        return new TestHost(app, logger, sink);
    }

    private static async Task AssertStartupFailsAsync<TException>(String contentRoot,
                                                                  IReadOnlyDictionary<String, String?> values,
                                                                  String expectedError)
        where TException : Exception
    {
        await using var host = CreateHost(contentRoot, values);

        // ReSharper disable once AccessToDisposedClosure
        var start = async () => await host.App.StartAsync();

        var exception = await start.Should().ThrowAsync<TException>();
        // The expected wording belongs to Kestrel and .NET, not to ShadowDrop: no application code inspects
        // certificates, so a framework update rewording these messages is the reason to revisit this assertion.
        exception.Which.ToString().Should().ContainEquivalentOf(expectedError);
    }

    private static async Task<HttpResponseMessage> GetHealthLiveAsync(WebApplication app, X509Certificate2? pinnedCertificate = null)
    {
        using var handler = new HttpClientHandler();
        if (pinnedCertificate is not null)
        {
            handler.ServerCertificateCustomValidationCallback = (_, presentedCertificate, _, _) =>
                presentedCertificate is not null
                && presentedCertificate.RawDataMemory.Span.SequenceEqual(pinnedCertificate.RawDataMemory.Span);
        }

        // ReSharper disable once ShortLivedHttpClient
        // ReSharper disable once UsingStatementResourceInitialization
        using var client = new HttpClient(handler)
        {
            BaseAddress = ResolveBaseAddress(app, pinnedCertificate is null ? Uri.UriSchemeHttp : Uri.UriSchemeHttps)
        };

        return await client.GetAsync("/health/live");
    }

    private static Uri ResolveBaseAddress(WebApplication app, String scheme)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
                           .Features.Get<IServerAddressesFeature>()
                           ?.Addresses;
        addresses.Should().NotBeNull();
        var boundAddress = addresses.Single(address => address.StartsWith($"{scheme}://", StringComparison.Ordinal));
        var boundUri = new Uri(boundAddress);
        return new UriBuilder(boundUri)
        {
            Host = IPAddress.Loopback.ToString()
        }.Uri;
    }

    /// <remarks>
    /// Mirrors the certificate generation in <c>tests/ShadowDrop.E2E.Tests/Infrastructure/SelfSignedTlsListener.cs</c>;
    /// the two test projects share no infrastructure assembly, so keep both in sync when the shape changes.
    /// </remarks>
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateSelfSignedCertificate(rsa);
        // The round-tripped certificate only exports test material and pins the client's trust callback, so the
        // key never needs to reach a platform key store.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx),
                                                null,
                                                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    private static X509Certificate2 CreateSelfSignedCertificate(RSA rsa)
    {
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                                              [new("1.3.6.1.5.5.7.3.1")],
                                              false));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeName.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeName.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static String CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shadowdrop-kestrel-https-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly Logger _logger;

        public TestHost(WebApplication app, Logger logger, RecordingSink sink)
        {
            App = app;
            _logger = logger;
            Sink = sink;
        }

        public WebApplication App { get; }

        public ILogger Logger => _logger;

        public RecordingSink Sink { get; }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await _logger.DisposeAsync();
        }
    }

    private sealed class RecordingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        /// <summary>
        /// Renders messages, structured properties, and exceptions so a leak is caught even when the value never
        /// reaches the rendered message text.
        /// </summary>
        public String Render()
        {
            var rendered = new StringBuilder();
            foreach (var logEvent in _events)
            {
                rendered.AppendLine(logEvent.RenderMessage());
                foreach (var property in logEvent.Properties)
                {
                    rendered.Append(property.Key).Append('=').Append(property.Value).AppendLine();
                }

                if (logEvent.Exception is not null)
                {
                    rendered.AppendLine(logEvent.Exception.ToString());
                }
            }

            return rendered.ToString();
        }

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<String, String?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<String, String?> values)
        {
            foreach (var (name, value) in values)
            {
                _previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
