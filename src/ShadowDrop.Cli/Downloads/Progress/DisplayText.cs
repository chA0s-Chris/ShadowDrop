// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// Helpers for rendering untrusted display values (file names, output paths) safely in single-line lifecycle output.
/// </summary>
internal static class DisplayText
{
    /// <summary>
    /// Collapses a value to a single line by replacing every control character (including CR and LF) with a space. File names and
    /// output paths originate from the server-supplied manifest/queue file, so this prevents a crafted value from splitting a
    /// deterministic lifecycle record on stderr or corrupting the rich Spectre progress layout.
    /// </summary>
    public static String SingleLine(String value)
    {
        if (String.IsNullOrEmpty(value))
        {
            return value;
        }

        foreach (var character in value)
        {
            if (Char.IsControl(character))
            {
                return Sanitize(value);
            }
        }

        return value;

        static String Sanitize(String value)
        {
            var buffer = value.ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                if (Char.IsControl(buffer[i]))
                {
                    buffer[i] = ' ';
                }
            }

            return new String(buffer);
        }
    }
}
