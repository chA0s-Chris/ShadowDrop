// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Cli.Interactive;
using ShadowDrop.Tests.Fakes;
using System.Net;

[NonParallelizable]
public sealed class CliApplicationTests
{
    [Test]
    public async Task InvokeAsync_ShouldAcceptShortHelpAlias_ForRootCommand()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["-h"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Should().Contain("Usage:");
    }

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
        standardError.ToString().Should().StartWith(".--// ShadowDrop v")
                     .And.EndWith("Interactive mode requires a terminal. Use non-interactive commands with explicit flags for scripted or piped environments."
                                  + Environment.NewLine);
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
        standardError.ToString().Should().StartWith(".--// ShadowDrop v")
                     .And.EndWith("Interactive mode requires a terminal. Use non-interactive commands with explicit flags for scripted or piped environments."
                                  + Environment.NewLine);
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
    public async Task InvokeAsync_ShouldKeepSimpleVersionHeader_ForHelpOutput_WhenNoBannerIsSpecified()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["--help", "--no-banner"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be($"ShadowDrop v{CliVersion.Current}");
        standardOut.ToString().Should().Contain("Usage:").And.NotContain(".--//");
    }

    [Test]
    public async Task InvokeAsync_ShouldPrependVersionHeader_ToRootHelpOutput()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["--help"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        var lines = standardOut.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be($"ShadowDrop v{CliVersion.Current}");
        lines.Should().Contain(line => line.Contains("Usage:"));
    }

    [Test]
    public async Task InvokeAsync_ShouldPrintPlainVersionOutput_AndNeverPrintBanner()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["--version"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"ShadowDrop v{CliVersion.Current}");
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldRejectQuestionMarkHelpAlias()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(["-?"], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Should().Contain("-?").And.NotContain(".--//");
    }

    [Test]
    public async Task InvokeAsync_ShouldRouteShareRevokeCommand()
    {
        var shareId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            AssertExactlyOneBanner(standardError.ToString());
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/api/admin/shares/{shareId}/revoke"));
            request.Headers.Authorization.Should().NotBeNull();
            request.Headers.Authorization!.Parameter.Should().Be("upload-token");
            return new(HttpStatusCode.NoContent);
        }));
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                  // ReSharper disable once AccessToDisposedClosure
                                                  _ => httpClient,
                                                  standardOut,
                                                  standardError,
                                                  new FakeInteractiveSession(),
                                                  TimeProvider.System,
                                                  new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
                                                  FixedTerminalCapabilityProvider.Plain);

        var exitCode = await CliApplication.InvokeAsync(["share", "revoke", shareId.ToString()], services, CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"share-revoked:{shareId}");
        // The banner is default-on for command execution and never corrupts the "share-revoked:<id>" stdout
        // contract, so it is expected on stderr instead of leaving stderr empty.
        var expectedBanner = new StringWriter();
        await CliBanner.WriteAsync(expectedBanner, FixedTerminalCapabilityProvider.Plain.DetectForStandardError(), CancellationToken.None);
        standardError.ToString().Should().Be(expectedBanner.ToString());
    }

    [Test]
    public async Task InvokeAsync_ShouldRouteTokenRevokeCommand_WithDedicatedAdminToken()
    {
        var credentialId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri($"https://shadowdrop.test/api/admin/upload-credentials/{credentialId}/revoke"));
            request.Headers.Authorization!.Parameter.Should().Be("admin-token", "token commands must use the dedicated admin token");
            return new(HttpStatusCode.NoContent);
        }));
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token", adminToken: "admin-token"),
                                                  // ReSharper disable once AccessToDisposedClosure
                                                  _ => httpClient,
                                                  standardOut,
                                                  standardError,
                                                  new FakeInteractiveSession(),
                                                  TimeProvider.System,
                                                  new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
                                                  FixedTerminalCapabilityProvider.Plain);

        var exitCode = await CliApplication.InvokeAsync(["--no-banner", "token", "revoke", credentialId.ToString()], services,
                                                        CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"token-revoked:{credentialId}");
    }

    [Test]
    public async Task InvokeAsync_ShouldShowHelp_WhenNoArgumentsAreProvided()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync([], CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(0);
        standardError.ToString().Should().BeEmpty();
        standardOut.ToString().Should().Contain("Usage:").And.NotContain("A command is required.");
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

    [Test]
    public async Task InvokeAsync_ShouldSuppressBanner_ForCommandOutput_WhenNoBannerIsSpecified()
    {
        var shareId = Guid.NewGuid();
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.NoContent)));
        var services = new CliApplicationServices(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                  // ReSharper disable once AccessToDisposedClosure
                                                  _ => httpClient,
                                                  standardOut,
                                                  standardError,
                                                  new FakeInteractiveSession(),
                                                  TimeProvider.System,
                                                  new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
                                                  FixedTerminalCapabilityProvider.Plain);

        var exitCode = await CliApplication.InvokeAsync(["share", "revoke", shareId.ToString(), "--no-banner"], services, CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should().Be($"share-revoked:{shareId}");
        standardError.ToString().Should().BeEmpty();
    }

    [TestCase(new[] { "upload" }, "Required argument missing for command: 'upload'.")]
    [TestCase(new[]
    {
        "download",
        "--output-root",
        "."
    }, "The --output-root option requires --queue.")]
    [TestCase(new[]
    {
        "queue",
        "create"
    }, "Specify the queue output path with --out.")]
    [TestCase(new[]
    {
        "share",
        "create",
        "file-id"
    }, "File id invalid or missing.")]
    [TestCase(new[]
    {
        "share",
        "revoke",
        "not-a-guid"
    }, "Share id invalid or missing.")]
    [TestCase(new[]
    {
        "upload",
        "raw",
        "payload.bin"
    }, "Server URL invalid or missing.")]
    [TestCase(new[]
    {
        "share",
        "cleanup"
    }, "Server URL invalid or missing.")]
    public async Task InvokeAsync_ShouldWriteExactlyOneStartupBanner_BeforeCommandValidation(String[] args, String expectedError)
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();

        var exitCode = await CliApplication.InvokeAsync(args, CreateServices(standardOut, standardError), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        var errorText = standardError.ToString();
        errorText.Should().StartWith(".--// ShadowDrop v").And.EndWith(expectedError + Environment.NewLine);
        errorText.Split(".--// ShadowDrop v", StringSplitOptions.None).Should().HaveCount(2);
    }

    [TestCase("upload")]
    [TestCase("download")]
    public async Task InvokeAsync_ShouldWriteExactlyOneStartupBanner_BeforeFirstInteractivePrompt(String command)
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var interactiveSession = new BoundaryInteractiveSession(() => AssertExactlyOneBanner(standardError.ToString()));

        var action = async () => await CliApplication.InvokeAsync([command, "--interactive"],
                                                                  CreateServices(standardOut, standardError, interactiveSession),
                                                                  CancellationToken.None);

        await action.Should().ThrowAsync<BoundaryObservedException>();
        interactiveSession.PromptCount.Should().Be(1);
    }

    private static void AssertExactlyOneBanner(String output)
    {
        output.Should().StartWith(".--// ShadowDrop v");
        output.Split(".--// ShadowDrop v", StringSplitOptions.None).Should().HaveCount(2);
    }

    private static CliApplicationServices CreateServices(StringWriter standardOut,
                                                         StringWriter standardError,
                                                         ICliInteractiveSession? interactiveSession = null) =>
        new(new(new StubConfigPathResolver(), new StubEnvironmentReader()),
            _ => new(new NeverCalledHandler()),
            standardOut,
            standardError,
            interactiveSession ?? new FakeInteractiveSession(),
            TimeProvider.System,
            new PlainDownloadProgressReporterFactory(standardOut, standardError, TimeProvider.System),
            FixedTerminalCapabilityProvider.Plain);

    private sealed class BoundaryInteractiveSession(Action assertBoundary) : ICliInteractiveSession
    {
        public Int32 PromptCount { get; private set; }
        public Boolean IsInteractiveSupported => true;

        private T ObserveBoundary<T>()
        {
            PromptCount++;
            assertBoundary();
            throw new BoundaryObservedException();
        }

        public Boolean PromptConfirmation(String prompt, Boolean defaultValue = false) => ObserveBoundary<Boolean>();

        public IReadOnlyList<T> PromptMultiSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull =>
            ObserveBoundary<IReadOnlyList<T>>();

        public T PromptSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull =>
            ObserveBoundary<T>();

        public String PromptText(String prompt, String? defaultValue = null, Boolean secret = false, Func<String, String?>? validate = null) =>
            ObserveBoundary<String>();

        public void ShowError(String message) => throw new AssertionException("A prompt should be the first interactive output.");

        public void ShowMessage(String message) => throw new AssertionException("A prompt should be the first interactive output.");

        public void ShowSummary(String title, IReadOnlyList<(String Label, String Value)> rows) =>
            throw new AssertionException("A prompt should be the first interactive output.");
    }

    private sealed class BoundaryObservedException : Exception;

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
