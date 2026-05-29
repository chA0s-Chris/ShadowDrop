// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

using Spectre.Console;

internal sealed class SpectreCliInteractiveSession : ICliInteractiveSession
{
    private readonly IAnsiConsole _console;

    public SpectreCliInteractiveSession(TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        _console = AnsiConsole.Create(new()
        {
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(standardError)
        });
    }

    public Boolean IsInteractiveSupported => !Console.IsInputRedirected
                                             && !Console.IsOutputRedirected
                                             && !Console.IsErrorRedirected
                                             && !String.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    public Boolean PromptConfirmation(String prompt, Boolean defaultValue = false) =>
        _console.Prompt(new ConfirmationPrompt(Markup.Escape(prompt))
        {
            DefaultValue = defaultValue
        });

    public IReadOnlyList<T> PromptMultiSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(displaySelector);

        var prompt = new MultiSelectionPrompt<T>()
                     .Title(Markup.Escape(title))
                     .UseConverter(item => Markup.Escape(displaySelector(item)));
        prompt.AddChoices(choices);
        return _console.Prompt(prompt);
    }

    public T PromptSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(displaySelector);

        var prompt = new SelectionPrompt<T>()
                     .Title(Markup.Escape(title))
                     .UseConverter(item => Markup.Escape(displaySelector(item)));
        prompt.AddChoices(choices);
        return _console.Prompt(prompt);
    }

    public String PromptText(String prompt, String? defaultValue = null, Boolean secret = false, Func<String, String?>? validate = null)
    {
        var textPrompt = new TextPrompt<String>(Markup.Escape(prompt));
        if (!String.IsNullOrWhiteSpace(defaultValue))
        {
            textPrompt.DefaultValue(defaultValue);
        }

        if (secret)
        {
            textPrompt.Secret();
        }

        if (validate is not null)
        {
            textPrompt.Validate(value =>
            {
                var error = validate(value);
                return error is null ? ValidationResult.Success() : ValidationResult.Error(error);
            });
        }

        return _console.Prompt(textPrompt);
    }

    public void ShowError(String message) => _console.MarkupLine($"[red]{Markup.Escape(message)}[/]");

    public void ShowMessage(String message) => _console.MarkupLine(Markup.Escape(message));

    public void ShowSummary(String title, IReadOnlyList<(String Label, String Value)> rows)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(rows);

        _console.Write(new Rule(Markup.Escape(title)));
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");

        foreach (var (label, value) in rows)
        {
            table.AddRow(Markup.Escape(label), Markup.Escape(value));
        }

        _console.Write(table);
    }
}
