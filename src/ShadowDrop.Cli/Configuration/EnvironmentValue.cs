// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

/// <summary>
/// Interprets environment-variable values consistently across CLI features: <c>1</c>, <c>true</c>, and
/// <c>yes</c> (case-insensitive) are truthy.
/// </summary>
internal static class EnvironmentValue
{
    public static Boolean IsTruthy(String? value)
    {
        var trimmed = value?.Trim();
        return String.Equals(trimmed, "1", StringComparison.Ordinal)
               || String.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
               || String.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
