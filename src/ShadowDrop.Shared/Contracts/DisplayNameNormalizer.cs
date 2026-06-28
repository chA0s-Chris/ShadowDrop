// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Normalizes recipient-facing display names so the API and the CLI resolve them identically. A name that is
/// <see langword="null"/>, empty, or whitespace collapses to <see langword="null"/> (meaning "no display name");
/// otherwise surrounding whitespace is trimmed.
/// </summary>
public static class DisplayNameNormalizer
{
    /// <summary>
    /// Normalizes the supplied display name.
    /// </summary>
    /// <param name="displayName">The raw display name, or <see langword="null"/>.</param>
    /// <returns>The trimmed display name, or <see langword="null"/> when it normalizes to empty.</returns>
    public static String? Normalize(String? displayName) =>
        String.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
}
