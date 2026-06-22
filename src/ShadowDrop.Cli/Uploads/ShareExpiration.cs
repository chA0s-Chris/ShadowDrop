// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Globalization;

/// <summary>
/// Parses share-expiration durations supplied on the command line.
/// </summary>
/// <remarks>
/// Accepts an integer followed by a unit suffix: <c>m</c> (minutes), <c>h</c> (hours), or <c>d</c> (days).
/// The end-to-end upload default is 7 days when no value is supplied.
/// </remarks>
internal static class ShareExpiration
{
    /// <summary>
    /// The default share expiration applied when the user supplies no explicit value.
    /// </summary>
    public static readonly TimeSpan Default = TimeSpan.FromDays(7);

    /// <summary>
    /// Attempts to parse an expiration duration such as <c>7d</c>, <c>12h</c>, or <c>30m</c>.
    /// </summary>
    /// <param name="value">The raw option value.</param>
    /// <param name="duration">The parsed positive duration when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid positive duration.</returns>
    public static Boolean TryParse(String? value, out TimeSpan duration)
    {
        duration = default;
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var unit = trimmed[^1];
        var numberText = trimmed[..^1];
        if (!Int64.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            return false;
        }

        try
        {
            duration = unit switch
            {
                'm' or 'M' => TimeSpan.FromMinutes(amount),
                'h' or 'H' => TimeSpan.FromHours(amount),
                'd' or 'D' => TimeSpan.FromDays(amount),
                _ => TimeSpan.Zero
            };
        }
        catch (OverflowException)
        {
            duration = default;
            return false;
        }

        return duration > TimeSpan.Zero;
    }
}
