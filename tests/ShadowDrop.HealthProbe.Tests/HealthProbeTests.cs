// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.HealthProbe;
using System.Net;

[TestFixture]
public sealed class HealthProbeTests
{
    [Test]
    public async Task RunAsync_ShouldReturnHealthyExitCode_ForSuccessfulResponse()
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var exitCode = await HealthProbe.RunAsync(client, new("http://localhost/health/ready"), TimeSpan.FromSeconds(1), CancellationToken.None);

        exitCode.Should().Be(HealthProbe.HealthyExitCode);
    }

    [Test]
    public async Task RunAsync_ShouldReturnUnhealthyExitCode_ForUnhealthyResponse()
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var exitCode = await HealthProbe.RunAsync(client, new("http://localhost/health/ready"), TimeSpan.FromSeconds(1), CancellationToken.None);

        exitCode.Should().Be(HealthProbe.UnhealthyExitCode);
    }

    [Test]
    public async Task RunAsync_ShouldReturnUnhealthyExitCode_ForConnectionFailure()
    {
        using var client = CreateClient((_, _) => throw new HttpRequestException("connection refused"));

        var exitCode = await HealthProbe.RunAsync(client, new("http://localhost/health/ready"), TimeSpan.FromSeconds(1), CancellationToken.None);

        exitCode.Should().Be(HealthProbe.UnhealthyExitCode);
    }

    [Test]
    public async Task RunAsync_ShouldReturnUnhealthyExitCode_WhenTimeoutExpires()
    {
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        });

        var act = async () => await HealthProbe.RunAsync(client,
                                                         new("http://localhost/health/ready"),
                                                         TimeSpan.FromMilliseconds(10),
                                                         CancellationToken.None);

        (await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1))).Which.Should().Be(HealthProbe.UnhealthyExitCode);
    }

    [TestCase(null, TestName = "No endpoint")]
    [TestCase("not-a-url", TestName = "Invalid endpoint")]
    [TestCase("file:///tmp/health", TestName = "Unsupported scheme")]
    public async Task Main_ShouldReturnInvalidArgumentsExitCode(String? argument)
    {
        var arguments = argument is null ? Array.Empty<String>() : new[] { argument };
        var exitCode = await Program.Main(arguments);

        exitCode.Should().Be(HealthProbe.InvalidArgumentsExitCode);
    }

    [Test]
    public async Task Main_ShouldReturnInvalidArgumentsExitCode_WhenTooManyArgumentsAreProvided()
    {
        var exitCode = await Program.Main(["http://localhost", "extra"]);

        exitCode.Should().Be(HealthProbe.InvalidArgumentsExitCode);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
        new(new StubHttpMessageHandler(response));

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            response(request, cancellationToken);
    }
}
