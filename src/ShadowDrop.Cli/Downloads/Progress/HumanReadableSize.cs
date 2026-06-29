// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

using System.Globalization;

/// <summary>
/// Formats byte counts, transfer speeds, and durations as human-readable text using decimal (1000-based) units.
/// </summary>
internal static class HumanReadableSize
{
    private static readonly String[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Formats a byte count using decimal units, e.g. <c>128.4 MB</c>. Values below 1000 bytes are reported as whole bytes.
    /// </summary>
    public static String FormatBytes(Int64 bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes < 1000)
        {
            return $"{bytes} B";
        }

        return FormatScaled(bytes, String.Empty);
    }

    /// <summary>
    /// Formats an elapsed duration, e.g. <c>2.1s</c>, <c>3m 4s</c>, or <c>1h 5m</c>.
    /// </summary>
    public static String FormatDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{(Int32)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        return $"{(Int32)elapsed.TotalHours}h {elapsed.Minutes}m";
    }

    /// <summary>
    /// Formats a transfer speed derived from <paramref name="bytes"/> over <paramref name="elapsed"/>, e.g. <c>61.1 MB/s</c>.
    /// </summary>
    public static String FormatSpeed(Int64 bytes, TimeSpan elapsed)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        // Guard against divide-by-zero for instantaneous downloads while keeping the result finite.
        var seconds = elapsed.TotalSeconds;
        if (seconds < 0.001)
        {
            seconds = 0.001;
        }

        var (value, unit) = Scale(bytes / seconds);
        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}/s";
    }

    private static String FormatScaled(Double value, String suffix)
    {
        var (scaled, unit) = Scale(value);
        return $"{scaled.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}{suffix}";
    }

    // Scales a value into decimal (1000-based) units. After the initial scale, a value in [999.95, 1000) would render as
    // "1000.0" at one decimal place, so roll over to the next unit to keep the output consistent (e.g. "1.0 MB" instead of "1000.0 KB").
    private static (Double Value, Int32 Unit) Scale(Double value)
    {
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        // Math.Round defaults to round-half-to-even, matching ToString("0.0"), so this fires exactly when the value would render as "1000.0".
        if (Math.Round(value, 1) >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return (value, unit);
    }
}
