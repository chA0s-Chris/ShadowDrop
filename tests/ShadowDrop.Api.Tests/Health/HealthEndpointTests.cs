// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Health;

using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api.Health;
using System.Net;

[TestFixture]
public sealed class HealthEndpointTests
{
    [Test]
    public async Task Liveness_ShouldReportProcessAvailability()
    {
        await using var app = await CreateApplicationAsync(new ManualReadinessCheck(false));
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Readiness_ShouldReturnOk_ForLocalPersistence()
    {
        await using var app = await CreateApplicationAsync(new LocalReadinessCheck());
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestCase(true, HttpStatusCode.OK)]
    [TestCase(false, HttpStatusCode.ServiceUnavailable)]
    public async Task Readiness_ShouldReflectMongoDependencyState(Boolean mongoAvailable, HttpStatusCode expectedStatusCode)
    {
        var readinessCheck = new ManualReadinessCheck(mongoAvailable);
        await using var app = await CreateApplicationAsync(readinessCheck);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(expectedStatusCode);
        readinessCheck.CallCount.Should().Be(1);
    }

    [Test]
    public async Task LegacyHealthEndpoint_ShouldNotBeMapped()
    {
        await using var app = await CreateApplicationAsync(new LocalReadinessCheck());
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<WebApplication> CreateApplicationAsync(IReadinessCheck readinessCheck)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IReadinessCheck>(readinessCheck);
        var app = builder.Build();
        app.MapHealthEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class ManualReadinessCheck(Boolean isReady) : IReadinessCheck
    {
        public Int32 CallCount { get; private set; }

        public Task<Boolean> IsReadyAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(isReady);
        }
    }
}
