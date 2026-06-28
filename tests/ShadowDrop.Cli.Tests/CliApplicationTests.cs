// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Tests.Fakes;
using System.Net;

[NonParallelizable]
public sealed class CliApplicationTests
{
    [Test]
    public async Task InvokeAsync_ShouldFailInteractiveDownloadImmediately_WhenTerminalSupportIsUnavailable()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession
        {
            IsInteractiveSupported = false
        };

        var exitCode = await CliApplication.InvokeAsync(["download", "--interactive"], CreateServices(standardOut, standardError, interactiveSession),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should()
                     .Be("Interactive mode requires a terminal. Use non-interactive commands with explicit flags for scripted or piped environments.");
    }

    [Test]
    public async Task InvokeAsync_ShouldFailInteractiveUploadImmediately_WhenTerminalSupportIsUnavailable()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new FakeInteractiveSession
        {
            IsInteractiveSupported = false
        };

        var exitCode = await CliApplication.InvokeAsync(["upload", "--interactive"], CreateServices(standardOut, standardError, interactiveSession),
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should()
                     .Be("Interactive mode requires a terminal. Use non-interactive commands with explicit flags for scripted or piped environments.");
    }

    [Test]
    public async Task InvokeAsync_ShouldHonorSeparatorBeforeHelpLikeOperands()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--", "--help"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("Server URL invalid or missing.")
                     .And.NotContain("ShadowDrop CLI")
                     .And.NotContain("Encrypt local files and upload encrypted content to ShadowDrop.");
    }

    [Test]
    public async Task InvokeAsync_ShouldRouteShareRevokeCommand()
    {
        var shareId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/api/admin/shares/{shareId}/revoke"));
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Parameter.Should().Be("upload-token");
            return new(HttpStatusCode.NoContent);
        }));
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                  httpClient,
                                                  standardOut,
                                                  standardError);

        var exitCode = await CliApplication.InvokeAsync(["share", "revoke", shareId.ToString()], services, CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"share-revoked:{shareId}");
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldStillTreatHelpFlagBeforeSeparatorAsHelpRequest()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["upload", "--help", "--", "ignored.bin"], CreateServices(standardOut, standardError),
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Should().Contain("Encrypt local files and upload encrypted content to ShadowDrop.")
                   .And.Contain("--server-url")
                   .And.Contain("--upload-token")
                   .And.Contain("--interactive");
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         FakeInteractiveSession? interactiveSession = null) =>
        new(new(new StubConfigPathResolver(), new StubEnvironmentReader()),
            new HttpClient(new NeverCalledHandler()),
            Stream.Null,
            standardOut,
            standardError,
            interactiveSession ?? new FakeInteractiveSession(),
            TimeProvider.System);

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }

    private sealed class StubConfigPathResolver : CliConfigPathResolver
    {
        public override String? GetConfigFilePath() => null;
    }

    private sealed class StubEnvironmentReader : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => null;
    }
}
