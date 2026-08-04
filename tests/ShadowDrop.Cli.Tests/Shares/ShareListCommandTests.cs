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

public sealed class ShareListCommandTests
{
    [Test]
    public async Task Failure_ShouldReturnOneWithGenericStderr_AndNoStdout()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("provider-secret", Encoding.UTF8, "text/plain")
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareListCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                  client,
                                                  standardOut,
                                                  standardError);

        var exitCode = await handler.ExecuteAsync(new(null, null, null, null, null, true), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share listing failed.{Environment.NewLine}")
                     .And.NotContain("provider-secret")
                     .And.NotContain("admin-secret");
    }

    [Test]
    public async Task HumanWriter_ShouldUseDeterministicFieldOrder()
    {
        var output = new StringWriter();

        await ShareListResultWriter.WriteAsync(Page(), false, output);

        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().Equal(
            "total-matching:1",
            "share:00000000-0000-0000-0000-000000000001 created=2026-08-03T10:00:00.0000000+00:00 "
            + "expires=2026-08-04T10:00:00.0000000+00:00 revoked=- statuses=active,cleanup-pending cleanup=pending "
            + "cleanup-attempt=- cleanup-failures=- files=2 ciphertext-bytes=42",
            "next-cursor:opaque");
    }

    [TestCase(0, null)]
    [TestCase(201, null)]
    [TestCase(null, "not-a-status")]
    [TestCase(null, "active,expired")]
    [TestCase(null, "")]
    public async Task InvalidOptions_ShouldFailWithoutIssuingRequest(Int32? pageSize, String? status)
    {
        // `null` here means `--status` was omitted, which is valid on its own; the page size drives those cases.
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var handler = new ShareListCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                  client,
                                                  standardOut,
                                                  standardError);

        var exitCode = await handler.ExecuteAsync(new(status is null ? null : [status], pageSize, null, null, null, true),
                                                  CancellationToken.None);

        exitCode.Should().Be(1);
        requests.Should().Be(0, "options the server would reject must not reach it");
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share listing failed.{Environment.NewLine}");
    }

    [Test]
    public async Task JsonMode_ShouldBuildRepeatedQuery_AndEmitExactlyOnePageValue()
    {
        var page = Page();
        Uri? requestUri = null;
        String? bearerToken = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            bearerToken = request.Headers.Authorization?.Parameter;
            return JsonResponse(HttpStatusCode.OK, page);
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                  client,
                                                  standardOut,
                                                  standardError);

        var exitCode = await CliApplication.InvokeAsync([
            "share", "list", "--status", ShareListStatuses.CleanupFailed, "--status", ShareListStatuses.Active,
            "--status", ShareListStatuses.Active, "--page-size", "200", "--cursor", "cursor/value", "--json", "--no-banner"
        ], services, CancellationToken.None);

        exitCode.Should().Be(0);
        requestUri.Should().Be(new Uri("https://shadowdrop.test/api/admin/shares?status=active&status=cleanup-failed&pageSize=200&cursor=cursor%2Fvalue"));
        bearerToken.Should().Be("admin-secret");
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        JsonSerializer.Deserialize(standardOut.ToString(), OperationalStatusJsonSerializerContext.Default.ShareListPageContract)
                      .Should().BeEquivalentTo(page);
    }

    [Test]
    public async Task ValuelessStatusOption_ShouldFail_RatherThanMatchEveryShare()
    {
        var requests = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requests++;
            return JsonResponse(HttpStatusCode.OK, Page());
        }));
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", adminToken: "admin-secret"),
                                                  client,
                                                  standardOut,
                                                  standardError);

        // A shell expanding an empty filter variable leaves a bare `--status`. Widening that to the whole inventory
        // would be the opposite of what the caller asked for, so it must fail instead. A following flag still parses
        // as a flag rather than being swallowed as the option's value.
        var exitCode = await CliApplication.InvokeAsync(["share", "list", "--status", "--json", "--no-banner"],
                                                        services,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        requests.Should().Be(0, "a valueless --status must never be widened into an unfiltered listing");
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Be($"Share listing failed.{Environment.NewLine}");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, ShareListPageContract page) =>
        new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(page,
                                                                 OperationalStatusJsonSerializerContext.Default.ShareListPageContract),
                                        Encoding.UTF8,
                                        "application/json")
        };

    private static ShareListPageContract Page() =>
        new(1,
            [
                new("00000000-0000-0000-0000-000000000001",
                    DateTimeOffset.Parse("2026-08-03T10:00:00Z"),
                    DateTimeOffset.Parse("2026-08-04T10:00:00Z"),
                    null,
                    [ShareListStatuses.Active, ShareListStatuses.CleanupPending],
                    ShareListCleanupStates.Pending,
                    null,
                    [],
                    2,
                    42)
            ],
            "opaque",
            1);
}
