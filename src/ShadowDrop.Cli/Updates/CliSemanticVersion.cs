// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Parses and compares semantic versions per SemVer 2.0.0, including prerelease precedence, so the update
/// feature has a single source of truth for "is this release newer than the installed CLI".
/// </summary>
/// <remarks>
/// Parsing tolerates the <c>v</c> prefix used by release tags (<c>v1.2.3</c>) because <see cref="CliVersion"/>
/// reports a bare semantic version while releases are tagged with the prefix. Build metadata (<c>+…</c>) is
/// accepted and ignored for precedence, as the specification requires.
/// </remarks>
internal sealed class CliSemanticVersion : IComparable<CliSemanticVersion>
{
    private readonly String[] _prereleaseIdentifiers;

    private CliSemanticVersion(Int32 major, Int32 minor, Int32 patch, String[] prereleaseIdentifiers)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prereleaseIdentifiers = prereleaseIdentifiers;
    }

    public Boolean IsPrerelease => _prereleaseIdentifiers.Length > 0;
    public Int32 Major { get; }
    public Int32 Minor { get; }
    public Int32 Patch { get; }

    public static Boolean TryParse([NotNullWhen(true)] String? text, [NotNullWhen(true)] out CliSemanticVersion? version)
    {
        version = null;
        if (String.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var remaining = text.Trim();
        if (remaining.StartsWith('v') || remaining.StartsWith('V'))
        {
            remaining = remaining[1..];
        }

        var buildMetadataIndex = remaining.IndexOf('+');
        if (buildMetadataIndex >= 0)
        {
            var buildMetadata = remaining[(buildMetadataIndex + 1)..];
            if (!AreValidBuildMetadataIdentifiers(buildMetadata))
            {
                return false;
            }

            remaining = remaining[..buildMetadataIndex];
        }

        String? prerelease = null;
        var prereleaseIndex = remaining.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            prerelease = remaining[(prereleaseIndex + 1)..];
            remaining = remaining[..prereleaseIndex];
        }

        var coreComponents = remaining.Split('.');
        if (coreComponents.Length != 3
            || !TryParseNumericComponent(coreComponents[0], out var major)
            || !TryParseNumericComponent(coreComponents[1], out var minor)
            || !TryParseNumericComponent(coreComponents[2], out var patch))
        {
            return false;
        }

        var prereleaseIdentifiers = Array.Empty<String>();
        if (prerelease is not null)
        {
            prereleaseIdentifiers = prerelease.Split('.');
            if (Array.Exists(prereleaseIdentifiers, static identifier => !IsValidPrereleaseIdentifier(identifier)))
            {
                return false;
            }
        }

        version = new(major, minor, patch, prereleaseIdentifiers);
        return true;
    }

    public override String ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{String.Join('.', _prereleaseIdentifiers)}" : $"{Major}.{Minor}.{Patch}";

    private static Boolean AreValidBuildMetadataIdentifiers(String buildMetadata) =>
        buildMetadata.Length > 0
        && buildMetadata.Split('.').All(static identifier =>
                                            identifier.Length > 0 &&
                                            identifier.All(static character => Char.IsAsciiLetterOrDigit(character) || character == '-'));

    private static Int32 ComparePrereleaseIdentifiers(String left, String right)
    {
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);

        if (leftIsNumeric && rightIsNumeric)
        {
            // Leading zeroes are rejected at parse time, so a longer numeric identifier is always larger;
            // comparing by length first also avoids any integer overflow concern.
            var lengthComparison = left.Length.CompareTo(right.Length);
            return lengthComparison != 0 ? lengthComparison : String.CompareOrdinal(left, right);
        }

        if (leftIsNumeric)
        {
            return -1;
        }

        if (rightIsNumeric)
        {
            return 1;
        }

        return String.CompareOrdinal(left, right);
    }

    private static Boolean IsNumeric(String identifier)
    {
        foreach (var character in identifier)
        {
            if (!Char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static Boolean IsValidPrereleaseIdentifier(String identifier)
    {
        if (identifier.Length == 0)
        {
            return false;
        }

        foreach (var character in identifier)
        {
            if (!Char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        // Numeric identifiers must not include leading zeroes.
        return !IsNumeric(identifier) || identifier.Length == 1 || identifier[0] != '0';
    }

    private static Boolean TryParseNumericComponent(String component, out Int32 value)
    {
        value = 0;
        if (component.Length == 0 || !IsNumeric(component) || (component.Length > 1 && component[0] == '0'))
        {
            return false;
        }

        return Int32.TryParse(component, out value);
    }

    public Int32 CompareTo(CliSemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        // A stable version has higher precedence than any prerelease of the same core version.
        if (_prereleaseIdentifiers.Length == 0 || other._prereleaseIdentifiers.Length == 0)
        {
            return other._prereleaseIdentifiers.Length.CompareTo(_prereleaseIdentifiers.Length);
        }

        var sharedLength = Math.Min(_prereleaseIdentifiers.Length, other._prereleaseIdentifiers.Length);
        for (var index = 0; index < sharedLength; index++)
        {
            comparison = ComparePrereleaseIdentifiers(_prereleaseIdentifiers[index], other._prereleaseIdentifiers[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _prereleaseIdentifiers.Length.CompareTo(other._prereleaseIdentifiers.Length);
    }
}
