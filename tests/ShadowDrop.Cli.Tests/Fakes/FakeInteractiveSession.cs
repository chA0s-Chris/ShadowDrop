// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Cli.Interactive;
using System.Collections;

internal sealed class FakeInteractiveSession : ICliInteractiveSession
{
    private readonly Queue<Boolean> _confirmationResponses = new();
    private readonly Queue<IList> _multiSelectionResponses = new();
    private readonly Queue<Object> _selectionResponses = new();
    private readonly Queue<String> _textResponses = new();

    public List<String> Errors { get; } = [];

    public List<String> Messages { get; } = [];

    public List<(String Title, IReadOnlyList<(String Label, String Value)> Rows)> Summaries { get; } = [];

    public List<(String Prompt, Boolean Secret)> TextPrompts { get; } = [];

    public Boolean IsInteractiveSupported { get; set; } = true;

    public void EnqueueConfirmation(Boolean value) => _confirmationResponses.Enqueue(value);

    public void EnqueueMultiSelection<T>(params T[] values) where T : notnull => _multiSelectionResponses.Enqueue(values);

    public void EnqueueSelection<T>(T value) where T : notnull => _selectionResponses.Enqueue(value);

    public void EnqueueTextResponse(String value) => _textResponses.Enqueue(value);

    public Boolean PromptConfirmation(String prompt, Boolean defaultValue = false)
    {
        if (_confirmationResponses.Count == 0)
        {
            throw new InvalidOperationException($"No confirmation response queued for '{prompt}'.");
        }

        return _confirmationResponses.Dequeue();
    }

    public IReadOnlyList<T> PromptMultiSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull
    {
        if (_multiSelectionResponses.Count == 0)
        {
            throw new InvalidOperationException($"No multi-selection queued for '{title}'.");
        }

        var response = _multiSelectionResponses.Dequeue();
        if (response is IEnumerable<Int32> indexes)
        {
            return indexes.Select(index => choices[index]).ToArray();
        }

        return response.Cast<T>().ToArray();
    }

    public T PromptSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull
    {
        if (_selectionResponses.Count == 0)
        {
            throw new InvalidOperationException($"No selection queued for '{title}'.");
        }

        var response = _selectionResponses.Dequeue();
        return response is Int32 index ? choices[index] : (T)response;
    }

    public String PromptText(String prompt, String? defaultValue = null, Boolean secret = false, Func<String, String?>? validate = null)
    {
        TextPrompts.Add((prompt, secret));
        if (_textResponses.Count == 0)
        {
            throw new InvalidOperationException($"No text response queued for '{prompt}'.");
        }

        return _textResponses.Dequeue();
    }

    public void ShowError(String message) => Errors.Add(message);

    public void ShowMessage(String message) => Messages.Add(message);

    public void ShowSummary(String title, IReadOnlyList<(String Label, String Value)> rows) => Summaries.Add((title, rows));
}
