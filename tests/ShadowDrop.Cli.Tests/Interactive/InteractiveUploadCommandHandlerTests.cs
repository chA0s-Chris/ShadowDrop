// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Interactive;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Tests.Fakes;

[NonParallelizable]
public sealed class InteractiveUploadCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ShouldAllowMultipleFiles_WhenUserAddsAnother()
    {
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse(MissingFilePath());
        session.EnqueueConfirmation(true); // Add another file?
        session.EnqueueTextResponse(MissingFilePath());
        session.EnqueueConfirmation(false);
        session.EnqueueSelection(2); // Expiration: 7 days
        session.EnqueueConfirmation(false); // Enable direct HTTP downloads?
        session.EnqueueConfirmation(false); // Require a download bearer token?
        var handler = CreateHandler(session, new NeverCalledHandler(),
                                    FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"));

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(1);
        session.TextPrompts.Count(prompt => prompt.Prompt == "Path to a file to upload:").Should().Be(2);
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenConfigurationIsInvalid()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, "{ not valid json");
        var standardError = new StringWriter();
        var handler = CreateHandler(new(), new NeverCalledHandler(),
                                    FakeConfiguration.Resolver(configFilePath: configPath), standardError);
        try
        {
            var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

            exitCode.Should().Be(1);
            standardError.ToString().Should().Contain("Configuration file invalid or unreadable.");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenTerminalUnsupported()
    {
        var standardError = new StringWriter();
        var session = new FakeInteractiveSession
        {
            IsInteractiveSupported = false
        };
        var handler = CreateHandler(session, new NeverCalledHandler(), standardError: standardError);

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Interactive mode requires a terminal.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldPromptForServerTokenAndFiles_ThenReportFailure()
    {
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse("https://shadowdrop.test"); // Server URL prompt
        session.EnqueueTextResponse("upload-token"); // Upload token prompt
        session.EnqueueTextResponse(MissingFilePath()); // First file path prompt
        session.EnqueueConfirmation(false); // Add another file?
        session.EnqueueSelection(2); // Expiration: 7 days
        session.EnqueueConfirmation(false); // Enable direct HTTP downloads?
        session.EnqueueConfirmation(false); // Require a download bearer token?
        var handler = CreateHandler(session, new NeverCalledHandler());

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(1);
        session.Summaries.Should().Contain(summary => summary.Title == "Upload plan");
        session.TextPrompts.Select(prompt => prompt.Prompt)
               .Should().Contain("ShadowDrop server URL:").And.Contain("Upload authorization token:").And.Contain("Path to a file to upload:");
    }

    [Test]
    public async Task ExecuteAsync_ShouldRepromptServerUrl_WhenFirstValueIsInvalid()
    {
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse("not-a-url"); // Invalid server URL -> reprompt
        session.EnqueueTextResponse("https://shadowdrop.test"); // Valid server URL
        session.EnqueueTextResponse("upload-token");
        session.EnqueueTextResponse(MissingFilePath());
        session.EnqueueConfirmation(false);
        session.EnqueueSelection(2); // Expiration: 7 days
        session.EnqueueConfirmation(false); // Enable direct HTTP downloads?
        session.EnqueueConfirmation(false); // Require a download bearer token?
        var handler = CreateHandler(session, new NeverCalledHandler());

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(1);
        session.TextPrompts.Count(prompt => prompt.Prompt == "ShadowDrop server URL:").Should().Be(2);
    }

    private static InteractiveUploadCommandHandler CreateHandler(FakeInteractiveSession session,
                                                                 HttpMessageHandler handler,
                                                                 CliConfigurationResolver? resolver = null,
                                                                 TextWriter? standardError = null) =>
        new(resolver ?? FakeConfiguration.Resolver(),
            new(handler),
            session,
            new StringWriter(),
            standardError ?? new StringWriter(),
            TimeProvider.System);

    private static String MissingFilePath() => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin");

    private static UploadCommandOptions Options() => new([], null, null, null, false, false, null, null, false, false, false, null, []);

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }
}
