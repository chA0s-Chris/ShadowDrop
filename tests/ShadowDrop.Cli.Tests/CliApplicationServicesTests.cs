// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Tls;

public sealed class CliApplicationServicesTests
{
    [Test]
    public void Constructor_ShouldDefaultStreamAndInteractiveSession_WhenUsingTextWriterOverload()
    {
        using var httpClient = new HttpClient();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var services = new CliApplicationServices(CreateResolver(), httpClient, standardOut, standardError);

        services.StandardOutStream.Should().BeSameAs(Stream.Null);
        services.StandardOut.Should().BeSameAs(standardOut);
        services.StandardError.Should().BeSameAs(standardError);
        services.InteractiveSession.Should().BeOfType<SpectreCliInteractiveSession>();
        services.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Test]
    public void Constructor_ShouldRetainProvidedStream_WhenUsingStreamOverload()
    {
        using var httpClient = new HttpClient();
        using var stream = new MemoryStream();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var services = new CliApplicationServices(CreateResolver(), httpClient, stream, standardOut, standardError);

        services.StandardOutStream.Should().BeSameAs(stream);
        services.InteractiveSession.Should().BeOfType<SpectreCliInteractiveSession>();
        services.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Test]
    public void CreateDefault_ShouldComposeServicesFromConsole()
    {
        var services = CliApplicationServices.CreateDefault();

        services.ConfigurationResolver.Should().NotBeNull();
        services.HttpClientFactory.Should().NotBeNull();
        services.StandardOutStream.Should().NotBeNull();
        services.InteractiveSession.Should().BeOfType<SpectreCliInteractiveSession>();
        services.TimeProvider.Should().BeSameAs(TimeProvider.System);

        using var httpClient = services.HttpClientFactory(CliTlsOptions.Default);
        httpClient.Should().NotBeNull();
        services.StandardOutStream.Dispose();
    }

    private static CliConfigurationResolver CreateResolver() => new(new(), new EnvironmentReader());
}
