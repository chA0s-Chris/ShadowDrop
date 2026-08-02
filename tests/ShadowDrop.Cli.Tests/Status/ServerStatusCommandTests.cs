// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Status;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Status;
using ShadowDrop.Cli.Tls;
using ShadowDrop.Contracts;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

[NonParallelizable]
public sealed class ServerStatusCommandTests
{
    [Test]
    public async Task AdminStatusHumanOutput_ShouldRenderCompleteStableProjection()
    {
        var status = new AdminServerStatusContract(
            1,
            true,
            true,
            OperationalStatusReasons.None,
            Capabilities(),
            "1.0.0",
            12,
            [
                new("storage", OperationalComponentStates.Ready, OperationalStatusReasons.None),
                new("metadata", OperationalComponentStates.Ready, OperationalStatusReasons.None)
            ],
            new("litedb", "filesystem"),
            new(2, 350),
            new(1, 2, 3, 4, 5, 6),
            new(DateTimeOffset.Parse("2026-08-02T12:34:56Z"), "success"),
            new(7),
            [OperationalStatusWarnings.StorageAccountingIncomplete]);
        using var client = CreateClient(_ => JsonResponse(HttpStatusCode.OK,
                                                          status,
                                                          OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract));
        var (services, standardOut, _) = CreateServices(client);

        var exitCode = await CliApplication.InvokeAsync(
            ["server", "status", "--verbose", "--admin-token", "admin", "--no-banner"],
            services,
            CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "server-url:https://shadowdrop.test/",
            "reachability:reachable",
            $"cli-version:{CliVersion.Current}",
            "protocol-compatible:true",
            "outcome:healthy",
            "protocol-version:1",
            "live:true",
            "ready:true",
            "reason:none",
            "capability-public-downloads:true",
            "capability-admin-operations:true",
            "capability-resumable-downloads:true",
            "capability-scoped-uploads:true",
            "build-version:1.0.0",
            "uptime-seconds:12",
            "metadata-provider:litedb",
            "storage-provider:filesystem",
            "component:metadata:ready:none",
            "component:storage:ready:none",
            "storage-files:2",
            "storage-ciphertext-bytes:350",
            "shares-active:1",
            "shares-expired:2",
            "shares-revoked:3",
            "shares-cleanup-pending:4",
            "shares-cleanup-failed:5",
            "shares-cleanup-completed:6",
            "cleanup-last-run-at-utc:2026-08-02T12:34:56.0000000+00:00",
            "cleanup-outcome:success",
            "resumable-sessions-active:7",
            "configuration-warning:storage-accounting-incomplete");
    }

    [Test]
    public async Task ConnectivityFailure_ShouldReturnExitThreeAndDeterministicJson()
    {
        using var client = new HttpClient(StubHttpMessageHandler.Throwing(new HttpRequestException("host detail")));
        var (services, standardOut, _) = CreateServices(client);

        var exitCode = await CliApplication.InvokeAsync(["server", "status", "--json", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(3);
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(ServerStatusOutcomes.Unreachable);
        standardOut.ToString().Should().NotContain("host detail");
    }

    [TestCase("public", HttpStatusCode.OK)]
    [TestCase("upload", HttpStatusCode.ServiceUnavailable)]
    [TestCase("admin", HttpStatusCode.OK)]
    public async Task FutureProtocolEnvelope_ShouldReturnProtocolIncompatibleWithoutDeserializingVersionOneProjection(
        String mode,
        HttpStatusCode statusCode)
    {
        using var client = CreateClient(_ => new(statusCode)
        {
            Content = new StringContent("{\"protocolVersion\":2,\"futureShape\":{\"state\":\"unknown\"}}",
                                        Encoding.UTF8,
                                        "application/json")
        });
        var (services, standardOut, _) = CreateServices(client);
        var arguments = mode switch
        {
            "public" => new[]
            {
                "server",
                "status",
                "--json",
                "--no-banner"
            },
            "upload" => new[]
            {
                "server",
                "status",
                "--upload-authorized",
                "--upload-token",
                "upload",
                "--json",
                "--no-banner"
            },
            "admin" => new[]
            {
                "server",
                "status",
                "--verbose",
                "--admin-token",
                "admin",
                "--json",
                "--no-banner"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported server status mode.")
        };

        var exitCode = await CliApplication.InvokeAsync(arguments, services, CancellationToken.None);

        exitCode.Should().Be(5);
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        using var document = JsonDocument.Parse(lines.Single());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(ServerStatusOutcomes.ProtocolIncompatible);
        document.RootElement.GetProperty("protocolCompatible").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.TryGetProperty("serverStatus", out _).Should().BeFalse();
    }

    [Test]
    public async Task JsonMode_ShouldProduceOneResult_ForParseAndTlsSetupFailures()
    {
        using var neverCalledClient = new HttpClient(StubHttpMessageHandler.Throwing(new AssertionException("HTTP must not run")));
        var (parseServices, parseOut, _) = CreateServices(neverCalledClient);

        var parseExit = await CliApplication.InvokeAsync(
            ["server", "status", "--json", "--unknown-option", "--no-banner"],
            parseServices,
            CancellationToken.None);

        parseExit.Should().Be(1);
        parseOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
        using (var document = JsonDocument.Parse(parseOut.ToString()))
        {
            document.RootElement.GetProperty("mode").GetString().Should().Be("public");
            document.RootElement.GetProperty("error").GetString().Should().Be("parse-error");
        }

        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(_ => throw new CliTlsConfigurationException("bad certificate"), standardOut, standardError);
        var tlsExit = await CliApplication.InvokeAsync(["server", "status", "--json", "--no-banner"], services, CancellationToken.None);

        tlsExit.Should().Be(1);
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
        using var tlsDocument = JsonDocument.Parse(standardOut.ToString());
        tlsDocument.RootElement.GetProperty("error").GetString().Should().Be("tls-configuration-invalid");
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, true, 1, 2, ServerStatusOutcomes.NotReady)]
    [TestCase(HttpStatusCode.OK, false, 1, 2, ServerStatusOutcomes.NotReady)]
    [TestCase(HttpStatusCode.OK, true, 2, 5, ServerStatusOutcomes.ProtocolIncompatible)]
    public async Task PublicStatus_ShouldApplyDocumentedExitPrecedence(
        HttpStatusCode httpStatus,
        Boolean ready,
        Int32 protocolVersion,
        Int32 expectedExit,
        String expectedOutcome)
    {
        var status = PublicStatus(ready, protocolVersion);
        using var client = CreateClient(_ => JsonResponse(httpStatus, status,
                                                          OperationalStatusJsonSerializerContext.Default.PublicServerStatusContract));
        var (services, standardOut, _) = CreateServices(client);

        var exitCode = await CliApplication.InvokeAsync(["server", "status", "--json", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(expectedExit);
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(expectedOutcome);
    }

    [Test]
    public async Task PublicStatus_ShouldIgnoreConfiguredCredentials_AndWriteOneJsonResult()
    {
        var status = PublicStatus(ready: true);
        HttpRequestMessage? capturedRequest = null;
        using var client = CreateClient(request =>
        {
            capturedRequest = request;
            return JsonResponse(HttpStatusCode.OK, status,
                                OperationalStatusJsonSerializerContext.Default.PublicServerStatusContract);
        });
        var (services, standardOut, _) = CreateServices(client, uploadToken: "configured-upload", adminToken: "configured-admin");

        var exitCode = await CliApplication.InvokeAsync(["server", "status", "--json", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        capturedRequest!.RequestUri.Should().Be(new Uri("https://shadowdrop.test/api/status"));
        capturedRequest.Headers.Authorization.Should().BeNull();
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);
        using var document = JsonDocument.Parse(lines.Single());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(ServerStatusOutcomes.Healthy);
        document.RootElement.GetProperty("serverStatus").GetProperty("protocolVersion").GetInt32().Should().Be(1);
    }

    [TestCase("http://operator:secret@shadowdrop.test")]
    [TestCase("https://shadowdrop.test?token=secret")]
    [TestCase("https://shadowdrop.test/#secret")]
    public async Task ServerUrlWithSensitiveUriParts_ShouldBeRejectedWithoutEchoingValue(String serverUrl)
    {
        using var neverCalledClient = new HttpClient(StubHttpMessageHandler.Throwing(new AssertionException("HTTP must not run")));
        var (services, standardOut, standardError) = CreateServices(neverCalledClient);

        var exitCode = await CliApplication.InvokeAsync(
            ["server", "status", "--server-url", serverUrl, "--json", "--no-banner"],
            services,
            CancellationToken.None);

        exitCode.Should().Be(1);
        var combinedOutput = standardOut + standardError.ToString();
        combinedOutput.Should().NotContain("secret").And.NotContain("operator");
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(ServerStatusOutcomes.UsageError);
        document.RootElement.GetProperty("error").GetString().Should().Be("configuration-invalid");
        document.RootElement.GetProperty("serverUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public void StatusApiTimeout_ShouldAccommodateAuthenticationAndStatusCollectionDeadlines()
    {
        ServerStatusApiClient.TotalTimeout.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Test]
    public async Task SyntacticallyValidButIncompleteResponse_ShouldReturnUnexpectedFailure()
    {
        using var client = CreateClient(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var (services, standardOut, _) = CreateServices(client);

        var exitCode = await CliApplication.InvokeAsync(["server", "status", "--json", "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        using var document = JsonDocument.Parse(lines.Single());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(ServerStatusOutcomes.UnexpectedFailure);
        document.RootElement.GetProperty("error").GetString().Should().Be("invalid-response");
        document.RootElement.GetProperty("reachable").GetBoolean().Should().BeTrue();
        document.RootElement.TryGetProperty("serverStatus", out _).Should().BeFalse();
    }

    [Test]
    public async Task UploadAndAdminModes_ShouldSelectExplicitEndpointAndCredential()
    {
        var uploadStatus = new UploadServerStatusContract(1,
                                                          true,
                                                          true,
                                                          OperationalStatusReasons.None,
                                                          Capabilities(),
                                                          new(10, 20, DateTimeOffset.Parse("2026-09-01T00:00:00Z")));
        var adminStatus = AdminStatus(ready: true);
        var requests = new List<(Uri Uri, String? Token)>();
        using var client = new HttpClient(new SequenceHttpMessageHandler(
                                              request =>
                                              {
                                                  requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter));
                                                  return JsonResponse(HttpStatusCode.OK, uploadStatus,
                                                                      OperationalStatusJsonSerializerContext.Default.UploadServerStatusContract);
                                              },
                                              request =>
                                              {
                                                  requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter));
                                                  return JsonResponse(HttpStatusCode.OK, adminStatus,
                                                                      OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract);
                                              }));
        var (services, standardOut, _) = CreateServices(client);

        var uploadExit = await CliApplication.InvokeAsync(
            ["server", "status", "--upload-authorized", "--upload-token", "upload", "--json", "--no-banner"],
            services,
            CancellationToken.None);
        var adminExit = await CliApplication.InvokeAsync(
            ["server", "status", "--verbose", "--admin-token", "admin", "--json", "--no-banner"],
            services,
            CancellationToken.None);

        uploadExit.Should().Be(0);
        adminExit.Should().Be(0);
        requests.Should().Equal((new Uri("https://shadowdrop.test/api/status/upload"), "upload"),
                                (new Uri("https://shadowdrop.test/api/admin/status"), "admin"));
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }

    [TestCase(HttpStatusCode.Unauthorized, 4, ServerStatusOutcomes.Unauthorized)]
    [TestCase(HttpStatusCode.NotFound, 1, ServerStatusOutcomes.CapabilityDisabled)]
    [TestCase(HttpStatusCode.ServiceUnavailable, 2, ServerStatusOutcomes.NotReady)]
    public async Task UploadStatus_ShouldClassifyResponseWithoutLeakingProjection(
        HttpStatusCode statusCode,
        Int32 expectedExit,
        String expectedOutcome)
    {
        using var client = CreateClient(_ => new(statusCode));
        var (services, standardOut, _) = CreateServices(client);

        var exitCode = await CliApplication.InvokeAsync(
            ["server", "status", "--upload-authorized", "--upload-token", "upload", "--json", "--no-banner"],
            services,
            CancellationToken.None);

        exitCode.Should().Be(expectedExit);
        using var document = JsonDocument.Parse(standardOut.ToString());
        document.RootElement.GetProperty("outcome").GetString().Should().Be(expectedOutcome);
        document.RootElement.TryGetProperty("serverStatus", out _).Should().BeFalse();
    }

    private static AdminServerStatusContract AdminStatus(Boolean ready) =>
        new(1,
            true,
            ready,
            ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable,
            Capabilities(),
            "1.0.0",
            12,
            [
                new("metadata", ready ? OperationalComponentStates.Ready : OperationalComponentStates.NotReady,
                    ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable)
            ],
            new("litedb", "filesystem"),
            new(0, 0),
            new(0, 0, 0, 0, 0, 0),
            new(null, "not-run"),
            new(null),
            []);

    private static StatusCapabilitiesContract Capabilities() => new(true, true, true, true);

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHttpMessageHandler(responder));

    private static (CliApplicationServices Services, StringWriter StandardOut, StringWriter StandardError) CreateServices(
        HttpClient client,
        String? uploadToken = null,
        String? adminToken = null)
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        return (CreateServices(_ => client, standardOut, standardError, uploadToken, adminToken), standardOut, standardError);
    }

    private static CliApplicationServices CreateServices(
        Func<CliTlsOptions, HttpClient> factory,
        StringWriter standardOut,
        StringWriter standardError,
        String? uploadToken = null,
        String? adminToken = null) =>
        new(FakeConfiguration.Resolver("https://shadowdrop.test", uploadToken, adminToken: adminToken),
            factory,
            standardOut,
            standardError,
            new FakeInteractiveSession(),
            TimeProvider.System,
            new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
            FixedTerminalCapabilityProvider.Plain);

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T value, JsonTypeInfo<T> typeInfo) =>
        new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, typeInfo), Encoding.UTF8, "application/json")
        };

    private static PublicServerStatusContract PublicStatus(Boolean ready, Int32 protocolVersion = 1) =>
        new(protocolVersion,
            true,
            ready,
            ready ? OperationalStatusReasons.None : OperationalStatusReasons.DependencyUnavailable,
            Capabilities());
}
