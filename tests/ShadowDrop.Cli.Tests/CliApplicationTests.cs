// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;

[NonParallelizable]
public sealed class CliApplicationTests
{
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
                   .And.Contain("--upload-token");
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut, StringWriter standardError) =>
        new(new(new StubConfigPathResolver(), new StubEnvironmentReader()), new HttpClient(new NeverCalledHandler()), standardOut, standardError);

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
