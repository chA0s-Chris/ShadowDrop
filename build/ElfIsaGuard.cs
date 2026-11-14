// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using System.Text.RegularExpressions;

/// <summary>
/// Rejects x64 Linux executables whose ELF notes demand more than the x86-64 baseline instruction set.
/// </summary>
/// <remarks>
/// glibc's loader enforces the <c>x86 ISA needed</c> property that the linker aggregates from every input
/// object, so a single tainted <c>crt1.o</c> from the build host's libc (for example the Ubuntu
/// <c>amd64v3</c> archive variant) makes the whole binary refuse to start on older CPUs. The guard fails
/// closed: without <c>llvm-readelf</c> or <c>readelf</c> it throws instead of skipping the check. This file
/// has no Nuke dependency so that the build tests can compile it directly.
/// </remarks>
internal static partial class ElfIsaGuard
{
    internal const String BaselineLevel = "x86-64-baseline";

    // Order is the preference: LLVM's tool ships with the release images' toolchain, GNU binutils is the
    // common fallback on developer machines.
    private static readonly String[] ReadElfTools = ["llvm-readelf", "readelf"];

    /// <summary>
    /// Returns the levels listed on every <c>x86 ISA needed</c> line of a <c>readelf -n</c> output, in
    /// order of appearance. <c>x86 ISA used</c> lines are ignored because they describe the instructions
    /// an object happens to contain, not what the loader enforces.
    /// </summary>
    /// <param name="readelfOutput">The text printed by <c>readelf -n</c>.</param>
    /// <returns>The trimmed levels; empty when the output carries no <c>x86 ISA needed</c> note.</returns>
    public static IReadOnlyList<String> ParseNeededLevels(String readelfOutput)
    {
        ArgumentNullException.ThrowIfNull(readelfOutput);

        return NeededLevelsRegex()
               .Matches(readelfOutput)
               .SelectMany(match => match.Groups["levels"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
               .ToArray();
    }

    /// <summary>
    /// Verifies that the executable requires no ISA level above <see cref="BaselineLevel"/>.
    /// </summary>
    /// <param name="artifactPath">The executable to inspect.</param>
    /// <param name="readNotes">
    /// Runs <c>&lt;tool&gt; -n &lt;artifactPath&gt;</c> and returns its output, or <c>null</c> when the tool
    /// is not installed. A tool that is installed but fails must throw.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Neither readelf tool is available, or the executable requires a level above baseline.
    /// </exception>
    public static void Verify(String artifactPath, Func<String, String, String?> readNotes)
    {
        ArgumentException.ThrowIfNullOrEmpty(artifactPath);
        ArgumentNullException.ThrowIfNull(readNotes);

        var notes = ReadElfTools.Select(tool => readNotes(tool, artifactPath))
                                .FirstOrDefault(output => output is not null);
        if (notes is null)
        {
            throw new InvalidOperationException(
                $"Cannot verify the ISA level of '{artifactPath}': neither '{String.Join("' nor '", ReadElfTools)}' is installed. " +
                "Install binutils or LLVM to publish x64 Linux artifacts.");
        }

        var offendingLevels = ParseNeededLevels(notes)
                              .Where(level => !String.Equals(level, BaselineLevel, StringComparison.Ordinal))
                              .Distinct(StringComparer.Ordinal)
                              .ToArray();
        if (offendingLevels.Length > 0)
        {
            throw new InvalidOperationException(
                $"'{artifactPath}' requires a CPU newer than {BaselineLevel} (offending 'x86 ISA needed' levels: {String.Join(", ", offendingLevels)}). " +
                "The build host's C runtime objects most likely carry an ISA marker; build inside the pinned cross-build image instead.");
        }
    }

    [GeneratedRegex(@"x86 ISA needed:(?<levels>[^\r\n]*)")]
    private static partial Regex NeededLevelsRegex();
}
