// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Interactive;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Interactive;
using Spectre.Console.Testing;

public sealed class SpectreCliInteractiveSessionTests
{
    [Test]
    public void IsInteractiveSupported_ShouldReturnFalse_WhenConsoleStreamsAreRedirected()
    {
        using var console = new TestConsole();
        var session = new SpectreCliInteractiveSession(console);

        // IsInteractiveSupported derives from System.Console redirection state, not the injected console.
        // Only assert when the test host has redirected a standard stream (the usual CI case); skip in a real terminal.
        Assume.That(Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected, Is.True);
        session.IsInteractiveSupported.Should().BeFalse();
    }

    [Test]
    public void PromptConfirmation_ShouldReturnFalse_WhenUserAnswersNo()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("n");
        var session = new SpectreCliInteractiveSession(console);

        session.PromptConfirmation("Proceed?", true).Should().BeFalse();
    }

    [Test]
    public void PromptConfirmation_ShouldReturnTrue_WhenUserAnswersYes()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("y");
        var session = new SpectreCliInteractiveSession(console);

        session.PromptConfirmation("Proceed?").Should().BeTrue();
    }

    [Test]
    public void PromptMultiSelection_ShouldReturnCheckedChoices()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var session = new SpectreCliInteractiveSession(console);

        var result = session.PromptMultiSelection("Pick some", ["alpha", "beta"], value => value);

        result.Should().Contain("alpha");
    }

    [Test]
    public void PromptSelection_ShouldReturnHighlightedChoice()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        var session = new SpectreCliInteractiveSession(console);

        var result = session.PromptSelection("Pick one", ["alpha", "beta"], value => value);

        result.Should().Be("alpha");
    }

    [Test]
    public void PromptText_ShouldAcceptSecretInput()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("s3cret");
        var session = new SpectreCliInteractiveSession(console);

        session.PromptText("Enter secret", secret: true).Should().Be("s3cret");
    }

    [Test]
    public void PromptText_ShouldRejectInvalidInput_ThenAcceptValid()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("bad");
        console.Input.PushTextWithEnter("good");
        var session = new SpectreCliInteractiveSession(console);

        var result = session.PromptText("Enter value", validate: value => value == "good" ? null : "must be good");

        result.Should().Be("good");
        console.Output.Should().Contain("must be good");
    }

    [Test]
    public void PromptText_ShouldReturnDefault_WhenInputIsEmpty()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter(String.Empty);
        var session = new SpectreCliInteractiveSession(console);

        session.PromptText("Enter value", "fallback").Should().Be("fallback");
    }

    [Test]
    public void PromptText_ShouldReturnTypedValue()
    {
        using var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("typed-value");
        var session = new SpectreCliInteractiveSession(console);

        session.PromptText("Enter value").Should().Be("typed-value");
    }

    [Test]
    public void ShowError_ShouldWriteMessageToConsole()
    {
        using var console = new TestConsole();
        var session = new SpectreCliInteractiveSession(console);

        session.ShowError("something failed");

        console.Output.Should().Contain("something failed");
    }

    [Test]
    public void ShowMessage_ShouldWriteMessageToConsole()
    {
        using var console = new TestConsole();
        var session = new SpectreCliInteractiveSession(console);

        session.ShowMessage("hello world");

        console.Output.Should().Contain("hello world");
    }

    [Test]
    public void ShowSummary_ShouldRenderTitleAndRows()
    {
        using var console = new TestConsole();
        var session = new SpectreCliInteractiveSession(console);

        session.ShowSummary("Upload summary", [("Server", "https://example.test"), ("Files", "2")]);

        console.Output.Should().Contain("Upload summary")
               .And.Contain("Server")
               .And.Contain("https://example.test")
               .And.Contain("Files");
    }
}
