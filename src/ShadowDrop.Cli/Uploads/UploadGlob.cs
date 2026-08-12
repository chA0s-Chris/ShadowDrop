// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A small, Native-AOT-safe matcher for the documented upload path glob contract.
/// </summary>
internal sealed class UploadGlob
{
    private readonly Boolean _ignoreCase;
    private readonly ImmutableArray<GlobSegment> _segments;

    private UploadGlob(ImmutableArray<GlobSegment> segments)
    {
        _segments = segments;
        _ignoreCase = OperatingSystem.IsWindows();
    }

    public static Boolean TryCreate(String pattern,
                                    [NotNullWhen(true)] out UploadGlob? glob,
                                    [NotNullWhen(false)] out String? error)
    {
        glob = null;
        if (String.IsNullOrEmpty(pattern))
        {
            error = "Glob patterns must not be empty.";
            return false;
        }

        var rawSegments = pattern.Split('/');
        if (rawSegments.Any(static segment => segment.Length == 0))
        {
            error = "Glob patterns must not contain empty path segments.";
            return false;
        }

        var segments = ImmutableArray.CreateBuilder<GlobSegment>(rawSegments.Length);
        foreach (var rawSegment in rawSegments)
        {
            if (rawSegment == "**")
            {
                segments.Add(GlobSegment.Recursive);
                continue;
            }

            if (!TryParseSegment(rawSegment, out var segment, out error))
            {
                return false;
            }

            segments.Add(segment);
        }

        glob = new(segments.ToImmutable());
        error = null;
        return true;
    }

    public Boolean IsMatch(String relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var pathSegments = relativePath.Replace(Path.DirectorySeparatorChar, '/').Split('/');
        Dictionary<(Int32 Pattern, Int32 Path), Boolean> memo = [];
        return MatchPath(0, 0);

        Boolean MatchPath(Int32 patternIndex, Int32 pathIndex)
        {
            if (memo.TryGetValue((patternIndex, pathIndex), out var cached))
            {
                return cached;
            }

            Boolean matched;
            if (patternIndex == _segments.Length)
            {
                matched = pathIndex == pathSegments.Length;
            }
            else if (_segments[patternIndex].IsRecursive)
            {
                matched = MatchPath(patternIndex + 1, pathIndex)
                          || (pathIndex < pathSegments.Length && MatchPath(patternIndex, pathIndex + 1));
            }
            else
            {
                matched = pathIndex < pathSegments.Length
                          && MatchSegment(_segments[patternIndex].Tokens, pathSegments[pathIndex])
                          && MatchPath(patternIndex + 1, pathIndex + 1);
            }

            memo[(patternIndex, pathIndex)] = matched;
            return matched;
        }
    }

    private static Boolean TryParseSegment(String rawSegment,
                                           out GlobSegment segment,
                                           [NotNullWhen(false)] out String? error)
    {
        var tokens = ImmutableArray.CreateBuilder<GlobToken>();
        for (var index = 0; index < rawSegment.Length; index++)
        {
            var character = rawSegment[index];
            if (character == '\\')
            {
                if (++index >= rawSegment.Length || rawSegment[index] is not ('*' or '?' or '\\'))
                {
                    segment = default;
                    error = "Glob escapes are limited to \\*, \\?, and \\\\.";
                    return false;
                }

                tokens.Add(new(GlobTokenKind.Literal, rawSegment[index]));
                continue;
            }

            if (character == '*')
            {
                if ((index + 1 < rawSegment.Length && rawSegment[index + 1] == '*')
                    || (index > 0 && rawSegment[index - 1] == '*'))
                {
                    segment = default;
                    error = "The ** wildcard is valid only as a complete path segment.";
                    return false;
                }

                tokens.Add(new(GlobTokenKind.Star));
                continue;
            }

            tokens.Add(character == '?' ? new(GlobTokenKind.Question) : new(GlobTokenKind.Literal, character));
        }

        segment = new(false, tokens.ToImmutable());
        error = null;
        return true;
    }

    private Boolean MatchSegment(ImmutableArray<GlobToken> tokens, String value)
    {
        Dictionary<(Int32 Token, Int32 Character), Boolean> memo = [];
        return Match(0, 0);

        Boolean Match(Int32 tokenIndex, Int32 characterIndex)
        {
            if (memo.TryGetValue((tokenIndex, characterIndex), out var cached))
            {
                return cached;
            }

            Boolean matched;
            if (tokenIndex == tokens.Length)
            {
                matched = characterIndex == value.Length;
            }
            else
            {
                var token = tokens[tokenIndex];
                matched = token.Kind switch
                {
                    GlobTokenKind.Star => Match(tokenIndex + 1, characterIndex)
                                          || (characterIndex < value.Length && Match(tokenIndex, characterIndex + 1)),
                    GlobTokenKind.Question => characterIndex < value.Length && Match(tokenIndex + 1, characterIndex + 1),
                    _ => characterIndex < value.Length
                         && MatchesLiteral(token.Literal, value[characterIndex])
                         && Match(tokenIndex + 1, characterIndex + 1)
                };
            }

            memo[(tokenIndex, characterIndex)] = matched;
            return matched;
        }
    }

    // Compares one code unit at a time. Ordinal-ignore-case string comparison uppercases per code unit, so the
    // invariant uppercase mapping here matches it without allocating a string for every character compared.
    private Boolean MatchesLiteral(Char expected, Char actual) =>
        _ignoreCase
            ? Char.ToUpperInvariant(expected) == Char.ToUpperInvariant(actual)
            : expected == actual;

    private readonly record struct GlobSegment(Boolean IsRecursive, ImmutableArray<GlobToken> Tokens)
    {
        public static GlobSegment Recursive { get; } = new(true, []);
    }

    private readonly record struct GlobToken(GlobTokenKind Kind, Char Literal = '\0');

    private enum GlobTokenKind
    {
        Literal,
        Star,
        Question
    }
}
