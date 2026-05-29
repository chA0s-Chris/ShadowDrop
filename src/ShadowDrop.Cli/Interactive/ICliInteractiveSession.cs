// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Interactive;

internal interface ICliInteractiveSession
{
    Boolean IsInteractiveSupported { get; }

    Boolean PromptConfirmation(String prompt, Boolean defaultValue = false);

    IReadOnlyList<T> PromptMultiSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull;

    T PromptSelection<T>(String title, IReadOnlyList<T> choices, Func<T, String> displaySelector) where T : notnull;

    String PromptText(String prompt, String? defaultValue = null, Boolean secret = false, Func<String, String?>? validate = null);

    void ShowError(String message);

    void ShowMessage(String message);

    void ShowSummary(String title, IReadOnlyList<(String Label, String Value)> rows);
}
