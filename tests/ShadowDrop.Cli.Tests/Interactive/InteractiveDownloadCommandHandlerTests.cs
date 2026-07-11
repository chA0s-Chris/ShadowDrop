// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Interactive;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Tests.Fakes;
using System.Net;

[NonParallelizable]
public sealed class InteractiveDownloadCommandHandlerTests
{
    private const String ValidKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private String _outputDirectory = String.Empty;

    [Test]
    public async Task ExecuteAsync_ShouldDownloadFile_WhenShareUrlAndKeyProvided()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueMultiSelection(0);
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session, fixture.CreateManifestResponse(), fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/d/token", fixture.ShareKey), CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
        session.Summaries.Should().ContainSingle(summary => summary.Title == "Download complete");
    }

    [Test]
    public async Task ExecuteAsync_ShouldDownloadMultipleFiles_ToOutputDirectory()
    {
        var first = EncryptedDownloadFixture.Create();
        var second = EncryptedDownloadFixture.Create();
        var session = new FakeInteractiveSession();
        session.EnqueueMultiSelection(0, 1);
        session.EnqueueTextResponse(_outputDirectory); // Output directory prompt (multi-file)
        var handler = CreateHandler(session,
                                    _ => first.CreateManifestResponse(first.CreateManifestFile("first.bin"), second.CreateManifestFile("second.bin")),
                                    _ => first.CreateDownloadResponse(),
                                    _ => second.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/d/token", first.ShareKey), CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(Path.Combine(_outputDirectory, "first.bin"))).Should().Equal(first.Plaintext);
        (await File.ReadAllBytesAsync(Path.Combine(_outputDirectory, "second.bin"))).Should().Equal(second.Plaintext);
        session.TextPrompts.Should().Contain(prompt => prompt.Prompt == "Output directory:");
    }

    [Test]
    public async Task ExecuteAsync_ShouldDownloadSpecificFile_WhenFileIdProvided()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session, fixture.CreateManifestResponse(), fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/d/token", fixture.ShareKey, fileId: fixture.FileId.ToString()),
            CancellationToken.None);

        exitCode.Should().Be(0);
        (await File.ReadAllBytesAsync(outputPath)).Should().Equal(fixture.Plaintext);
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenConfigurationIsInvalid()
    {
        var configPath = Path.Combine(_outputDirectory, "config.json");
        await File.WriteAllTextAsync(configPath, "{ not valid json");
        var standardError = new StringWriter();
        var handler = new InteractiveDownloadCommandHandler(FakeConfiguration.Resolver(configFilePath: configPath),
                                                            new(new NeverCalledHandler()), new FakeInteractiveSession(), standardError);

        var exitCode = await handler.ExecuteAsync(Options("plain-token", ValidKey), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Configuration file invalid or unreadable.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenQueuePathProvided()
    {
        var standardError = new StringWriter();
        var handler = new InteractiveDownloadCommandHandler(FakeConfiguration.Resolver(), new(), new FakeInteractiveSession(), standardError);

        var exitCode = await handler.ExecuteAsync(Options(queuePath: new("queue.json")), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("The --interactive option cannot be combined with --queue.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenShareUrlPathIsInvalid()
    {
        var standardError = new StringWriter();
        var session = new FakeInteractiveSession();
        var handler = new InteractiveDownloadCommandHandler(FakeConfiguration.Resolver(), new(new NeverCalledHandler()), session,
                                                            standardError);

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/not-a-share", ValidKey), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Share token invalid or missing.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldFail_WhenTerminalUnsupported()
    {
        var standardError = new StringWriter();
        var session = new FakeInteractiveSession
        {
            IsInteractiveSupported = false
        };
        var handler = new InteractiveDownloadCommandHandler(FakeConfiguration.Resolver(), new(), session, standardError);

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(1);
        standardError.ToString().Should().Contain("Interactive mode requires a terminal.");
    }

    [Test]
    public async Task ExecuteAsync_ShouldPromptForBearerToken_WhenManifestRequiresAuthorization()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse("download-bearer"); // Bearer token prompt after first 403
        session.EnqueueMultiSelection(0);
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session,
                                    _ => new(HttpStatusCode.Forbidden),
                                    _ => fixture.CreateManifestResponse(),
                                    _ => fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/d/token", fixture.ShareKey), CancellationToken.None);

        exitCode.Should().Be(0);
        session.TextPrompts.Should().Contain(prompt => prompt.Prompt == "Download bearer token:");
    }

    [Test]
    public async Task ExecuteAsync_ShouldPromptForServerUrl_WhenTokenSuppliedWithoutConfiguredServer()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse("https://shadowdrop.test"); // Server URL prompt (no configured server)
        session.EnqueueMultiSelection(0);
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session, fixture.CreateManifestResponse(), fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(Options("plain-token", fixture.ShareKey), CancellationToken.None);

        exitCode.Should().Be(0);
        session.TextPrompts.Should().Contain(prompt => prompt.Prompt == "ShadowDrop server URL:");
    }

    [Test]
    public async Task ExecuteAsync_ShouldPromptForShareKeyAndShareId_WhenMissing()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueTextResponse(fixture.ShareKey); // Share key prompt
        session.EnqueueTextResponse("https://shadowdrop.test/d/token"); // Share URL prompt
        session.EnqueueMultiSelection(0);
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session, fixture.CreateManifestResponse(), fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(Options(), CancellationToken.None);

        exitCode.Should().Be(0);
        session.TextPrompts.Should().Contain(prompt => prompt.Prompt == "Share key:" && prompt.Secret);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRepromptForFiles_WhenNoneSelected()
    {
        var fixture = EncryptedDownloadFixture.Create();
        var outputPath = Path.Combine(_outputDirectory, "result.bin");
        var session = new FakeInteractiveSession();
        session.EnqueueMultiSelection<Int32>(); // empty selection -> error + reprompt
        session.EnqueueMultiSelection(0);
        session.EnqueueTextResponse(outputPath);
        var handler = CreateHandler(session, fixture.CreateManifestResponse(), fixture.CreateDownloadResponse());

        var exitCode = await handler.ExecuteAsync(
            Options("https://shadowdrop.test/d/token", fixture.ShareKey), CancellationToken.None);

        exitCode.Should().Be(0);
        session.Errors.Should().Contain("Select at least one file.");
    }

    [SetUp]
    public void SetUp()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"sd-interactive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }

    private static InteractiveDownloadCommandHandler CreateHandler(FakeInteractiveSession session,
                                                                   params Func<HttpRequestMessage, HttpResponseMessage>[] responses) =>
        new(FakeConfiguration.Resolver(), new(new SequenceHttpMessageHandler(responses)), session, new StringWriter());

    private static InteractiveDownloadCommandHandler CreateHandler(FakeInteractiveSession session, params HttpResponseMessage[] responses) =>
        CreateHandler(session, responses.Select<HttpResponseMessage, Func<HttpRequestMessage, HttpResponseMessage>>(response => _ => response).ToArray());

    private static DownloadCommandOptions Options(String? shareToken = null,
                                                  String? shareKey = null,
                                                  FileInfo? queuePath = null,
                                                  String? fileId = null) =>
        new(shareToken, null, fileId, queuePath, null, null, shareKey, null, null);

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("HTTP client should not have been called.");
    }
}
