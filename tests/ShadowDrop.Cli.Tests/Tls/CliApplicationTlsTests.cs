// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Tls;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Tls;
using ShadowDrop.Tests.Fakes;

[NonParallelizable]
public sealed class CliApplicationTlsTests
{
    [Test]
    public async Task InvokeAsync_ShouldFail_WhenCaCertFileIsMissing()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var missingPath = Path.Combine(Path.GetTempPath(), $"shadowdrop-missing-{Guid.NewGuid():N}.pem");
        var services = CreateServices(standardOut, standardError, CliHttpClientFactory.CreateClient);

        var exitCode = await CliApplication.InvokeAsync(["download", "share-token", "--cacert", missingPath], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("was not found").And.Contain(missingPath);
    }

    [Test]
    public async Task InvokeAsync_ShouldReject_WhenCaCertAndInsecureAreCombined()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut, standardError, _ => new(new NeverCalledHandler()));

        var exitCode = await CliApplication.InvokeAsync(["download", "share-token", "--cacert", "ca.pem", "--insecure"], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("--cacert and --insecure options cannot be combined");
    }

    [Test]
    public async Task InvokeAsync_ShouldReject_WhenCaCertFlagAndInsecureEnvironmentAreCombined()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut, standardError, _ => new(new NeverCalledHandler()),
                                      new Dictionary<String, String?>
                                      {
                                          ["SHADOWDROP_INSECURE"] = "1"
                                      });

        var exitCode = await CliApplication.InvokeAsync(["download", "share-token", "--cacert", "ca.pem"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("--cacert and --insecure options cannot be combined");
    }

    [Test]
    public async Task InvokeAsync_ShouldWarnOnStandardError_WhenInsecureIsActive()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var services = CreateServices(standardOut, standardError, _ => new(new NeverCalledHandler()));

        // No --server-url is supplied, so the command fails before any network call, but the warning is emitted first.
        var exitCode = await CliApplication.InvokeAsync(["download", "share-token", "--insecure"], services, CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("WARNING").And.Contain("--insecure");
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         Func<CliTlsOptions, HttpClient> httpClientFactory,
                                                         IReadOnlyDictionary<String, String?>? environment = null) =>
        new(new(new StubConfigPathResolver(), new StubEnvironmentReader(environment ?? new Dictionary<String, String?>())),
            httpClientFactory,
            Stream.Null,
            standardOut,
            standardError,
            new FakeInteractiveSession(),
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

    private sealed class StubEnvironmentReader(IReadOnlyDictionary<String, String?> values) : IEnvironmentReader
    {
        public String? GetEnvironmentVariable(String variableName) => values.GetValueOrDefault(variableName);
    }
}
