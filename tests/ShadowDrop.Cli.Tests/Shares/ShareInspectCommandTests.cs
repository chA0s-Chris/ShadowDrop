// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Contracts;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Text;
using System.Text.Json;

public sealed class ShareInspectCommandTests
{
    [Test]
    public async Task DefaultRequest_ShouldOmitFilenameQuery_AndHumanOutputShouldRenderNulls()
    {
        Uri? requestUri = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return JsonResponse(HttpStatusCode.OK, Inspection());
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareInspectCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                     client,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new("80000000-0000-0000-0000-000000000001", null, null, false, false),
                                                  CancellationToken.None);

        exitCode.Should().Be(0);
        requestUri.Should().Be(new Uri("https://shadowdrop.test/api/admin/shares/80000000-0000-0000-0000-000000000001"));
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "share:80000000-0000-0000-0000-000000000001",
            "created:2026-08-07T10:00:00.0000000+00:00",
            "expires:2026-08-08T10:00:00.0000000+00:00",
            "revoked:-",
            "statuses:active,cleanup-pending",
            "cleanup:pending",
            "cleanup-attempt:-",
            "cleanup-failures:-",
            "files:1",
            "ciphertext-bytes:42",
            "file:a0000000-0000-0000-0000-000000000001 ciphertext-bytes=42 retention=retained original-filename=null display-name=null");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidId_ShouldFailLocallyWithoutIssuingRequest(String? shareId)
    {
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requests++;
            return JsonResponse(HttpStatusCode.OK, Inspection());
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareInspectCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                     client,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new(shareId, null, null, false, true), CancellationToken.None);

        exitCode.Should().Be(1);
        requests.Should().Be(0);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share inspection failed.{Environment.NewLine}");
    }

    [Test]
    public async Task JsonMode_ShouldCanonicalizeId_SendOptIn_AndWriteExactlyOneValue()
    {
        Uri? requestUri = null;
        String? bearerToken = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            bearerToken = request.Headers.Authorization?.Parameter;
            return JsonResponse(HttpStatusCode.OK, Inspection());
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                  client,
                                                  standardOut,
                                                  standardError);

        var exitCode = await CliApplication.InvokeAsync([
            "share", "inspect", "{80000000-0000-0000-0000-000000000001}", "--include-filenames", "--json", "--no-banner"
        ], services, CancellationToken.None);

        exitCode.Should().Be(0);
        requestUri.Should().Be(new Uri(
                                   "https://shadowdrop.test/api/admin/shares/80000000-0000-0000-0000-000000000001?includeFilenames=true"));
        bearerToken.Should().Be("admin-secret");
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        JsonSerializer.Deserialize(standardOut.ToString(), OperationalStatusJsonSerializerContext.Default.ShareInspectionContract)
                      .Should().BeEquivalentTo(Inspection());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MalformedOrFutureResponse_ShouldReturnGenericFailure(Boolean futureProtocol)
    {
        var content = futureProtocol
            ? JsonSerializer.Serialize(
                Inspection() with
                {
                    ProtocolVersion = 2
                },
                OperationalStatusJsonSerializerContext.Default.ShareInspectionContract)
            : "not-json";
        using var client = new HttpClient(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareInspectCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                     client,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new("80000000-0000-0000-0000-000000000001", null, null, false, true),
                                                  CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share inspection failed.{Environment.NewLine}");
    }

    [Test]
    public async Task NotFound_ShouldReturnSixWithDiagnosticOnlyOnStderr()
    {
        var error = new OperationalErrorContract(OperationalErrorReasons.NotFound);
        using var client = new HttpClient(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(JsonSerializer.Serialize(error,
                                                                 OperationalStatusJsonSerializerContext.Default.OperationalErrorContract),
                                        Encoding.UTF8,
                                        "application/json")
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareInspectCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                     client,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new("80000000-0000-0000-0000-000000000001", null, null, false, true),
                                                  CancellationToken.None);

        exitCode.Should().Be(6);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share not found.{Environment.NewLine}");
    }

    private static ShareInspectionContract Inspection() =>
        new(1,
            "80000000-0000-0000-0000-000000000001",
            DateTimeOffset.Parse("2026-08-07T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"),
            null,
            [ShareListStatuses.Active, ShareListStatuses.CleanupPending],
            ShareListCleanupStates.Pending,
            null,
            [],
            1,
            42,
            [new("a0000000-0000-0000-0000-000000000001", 42, ShareFileRetentionStates.Retained, null, null)]);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, ShareInspectionContract inspection) =>
        new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(
                                            inspection,
                                            OperationalStatusJsonSerializerContext.Default.ShareInspectionContract),
                                        Encoding.UTF8,
                                        "application/json")
        };
}
