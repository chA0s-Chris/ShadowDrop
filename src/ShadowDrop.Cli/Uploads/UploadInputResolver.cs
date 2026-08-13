// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

internal sealed record UploadInputResolution(
    ImmutableArray<UploadSelection> Selections,
    Int32 ExcludedFileCount,
    ImmutableArray<UploadInputDiagnostic> Diagnostics,
    ImmutableArray<UploadInputError> Errors)
{
    public Boolean IsValid => Errors.IsEmpty && !Selections.IsEmpty;
}

internal sealed record UploadInputError(String Message, UploadSelectionOrigin Origin);

internal sealed record UploadInputDiagnostic(String Message);

internal static class UploadInputResolver
{
    private static readonly StringComparer MatchPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static UploadInputResolution Resolve(IReadOnlyList<FileInfo> existingFiles,
                                                IReadOnlyList<String> inputPaths,
                                                Boolean recursive,
                                                IReadOnlyList<String> includePatterns,
                                                IReadOnlyList<String> excludePatterns,
                                                IReadOnlyList<String> filesFrom,
                                                String workingDirectory,
                                                TextReader standardInput)
    {
        ArgumentNullException.ThrowIfNull(existingFiles);
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(includePatterns);
        ArgumentNullException.ThrowIfNull(excludePatterns);
        ArgumentNullException.ThrowIfNull(filesFrom);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(standardInput);

        if (!TryCreateGlobs(includePatterns, "--include", out var includes, out var patternError))
        {
            return new([], 0, [], [patternError]);
        }

        if (!TryCreateGlobs(excludePatterns, "--exclude", out var excludes, out patternError))
        {
            return new([], 0, [], [patternError]);
        }

        if (filesFrom.Count(static source => source == "-") > 1)
        {
            return Invalid("--files-from - may appear only once.");
        }

        List<InputRecord> records =
        [
            .. existingFiles.Select(static file => new InputRecord(file.FullName, UploadSelectionOrigin.CommandLine)),
            .. inputPaths.Select(static path => new InputRecord(path, UploadSelectionOrigin.CommandLine))
        ];

        foreach (var source in filesFrom)
        {
            if (!TryReadList(source, workingDirectory, standardInput, out var listedRecords, out var listError))
            {
                return new([], 0, [], [listError]);
            }

            records.AddRange(listedRecords);
        }

        List<UploadSelection> selections = [];
        List<UploadInputDiagnostic> diagnostics = [];
        var excluded = 0;
        foreach (var record in records)
        {
            if (!TryResolvePath(record.Path, workingDirectory, out var fullPath, out var pathError))
            {
                return Invalid(new(pathError, record.Origin), diagnostics);
            }

            if (!Directory.Exists(fullPath))
            {
                selections.Add(new(new(fullPath), record.Origin));
                continue;
            }

            if (!recursive)
            {
                return Invalid(new($"The input path '{fullPath}' is a directory. Pass --recursive to include its files.", record.Origin),
                               diagnostics);
            }

            if (!TryExpandDirectory(fullPath, record.Origin, includes, excludes, out var expanded, out var directoryExcluded,
                                    out var directoryDiagnostics, out var expansionError))
            {
                return Invalid(expansionError, diagnostics);
            }

            excluded += directoryExcluded;
            diagnostics.AddRange(directoryDiagnostics);
            if (expanded.Count == 0 && directoryDiagnostics.Count == 0)
            {
                return Invalid(new($"The directory '{fullPath}' did not select any files.", record.Origin), diagnostics);
            }

            selections.AddRange(expanded);
        }

        // Diagnostics survive an invalid resolution: a run that selects nothing because every candidate was
        // an excluded link must still name those links rather than report an unexplained empty selection.
        return selections.Count == 0
            ? Invalid(new("No input files were selected.", UploadSelectionOrigin.CommandLine), diagnostics, excluded)
            : new([.. selections], excluded, [.. diagnostics], []);
    }

    private static UploadInputResolution Invalid(String message) =>
        new([], 0, [], [new(message, UploadSelectionOrigin.CommandLine)]);

    private static UploadInputResolution Invalid(UploadInputError error,
                                                 IReadOnlyList<UploadInputDiagnostic> diagnostics,
                                                 Int32 excluded = 0) =>
        new([], excluded, [.. diagnostics], [error]);

    private static Boolean TryCreateGlobs(IReadOnlyList<String> patterns,
                                          String optionName,
                                          out ImmutableArray<UploadGlob> globs,
                                          [NotNullWhen(false)] out UploadInputError? error)
    {
        var builder = ImmutableArray.CreateBuilder<UploadGlob>(patterns.Count);
        foreach (var pattern in patterns)
        {
            if (!UploadGlob.TryCreate(pattern, out var glob, out var globError))
            {
                globs = [];
                error = new($"Invalid {optionName} pattern '{pattern}': {globError}", UploadSelectionOrigin.CommandLine);
                return false;
            }

            builder.Add(glob);
        }

        globs = builder.ToImmutable();
        error = null;
        return true;
    }

    private static Boolean TryExpandDirectory(String root,
                                              UploadSelectionOrigin origin,
                                              ImmutableArray<UploadGlob> includes,
                                              ImmutableArray<UploadGlob> excludes,
                                              out IReadOnlyList<UploadSelection> selections,
                                              out Int32 excluded,
                                              out IReadOnlyList<UploadInputDiagnostic> diagnostics,
                                              [NotNullWhen(false)] out UploadInputError? error)
    {
        try
        {
            List<(String FullPath, String RelativePath, FileAttributes Attributes)> discovered = [];
            List<UploadInputDiagnostic> selectionDiagnostics = [];
            Stack<String> pending = new();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                // Whatever cleared this directory happened before it reached the stack, and the enumeration below
                // resolves it by path again. Re-checking here keeps the check adjacent to its use instead of
                // separated by the rest of the traversal, so a path swapped in between is caught rather than
                // followed. This narrows the window; closing it needs handle-based enumeration that no-follows
                // across the whole read, which the BCL does not expose.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    selections = [];
                    excluded = 0;
                    diagnostics = [];
                    error = new($"The directory '{directory}' is a link and will not be traversed.", origin);
                    return false;
                }

                // Enumerating infos rather than paths keeps the attributes the enumeration already read, so each
                // entry is classified without a second stat call.
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    var attributes = entry.Attributes;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(entry.FullName);
                        }

                        continue;
                    }

                    var relativePath = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    discovered.Add((entry.FullName, relativePath, attributes));
                }
            }

            if (!RecursiveLinkBoundary.TryResolveDirectoryRoot(root, out var resolvedRoot))
            {
                selections = [];
                excluded = 0;
                diagnostics = [];
                error = new($"The directory '{root}' could not be resolved to a physical path.", origin);
                return false;
            }

            discovered.Sort(static (left, right) => MatchPathComparer.Compare(left.RelativePath, right.RelativePath));
            List<UploadSelection> selected = [];
            excluded = 0;
            foreach (var file in discovered)
            {
                var included = includes.IsEmpty || includes.Any(glob => glob.IsMatch(file.RelativePath));
                var excludedByPattern = excludes.Any(glob => glob.IsMatch(file.RelativePath));
                if (!included || excludedByPattern)
                {
                    excluded++;
                    continue;
                }

                // The attributes come from the enumeration above, so an ordinary file reaches the selection
                // without a second stat call; only a reparse point pays for resolution.
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0
                    && !RecursiveLinkBoundary.TryValidateFile(new(file.FullPath), resolvedRoot))
                {
                    excluded++;
                    selectionDiagnostics.Add(
                        new($"The file link '{file.FullPath}' does not resolve within the recursive upload root and will not be uploaded."));
                    continue;
                }

                // Every recursive selection carries its root, not just the entries that are links right now.
                // An ordinary file swapped for a link after discovery is then caught by the same check
                // before it is opened for encryption.
                selected.Add(new(new(file.FullPath), origin, file.RelativePath, resolvedRoot));
            }

            selections = selected;
            diagnostics = selectionDiagnostics;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            selections = [];
            excluded = 0;
            diagnostics = [];
            error = new($"The directory '{root}' could not be enumerated.", origin);
            return false;
        }
    }

    private static Boolean TryReadList(String source,
                                       String workingDirectory,
                                       TextReader standardInput,
                                       [NotNullWhen(true)] out IReadOnlyList<InputRecord>? records,
                                       [NotNullWhen(false)] out UploadInputError? error)
    {
        String content;
        UploadSelectionOrigin sourceOrigin;
        if (source == "-")
        {
            sourceOrigin = new("stdin");
            try
            {
                content = standardInput.ReadToEnd();
            }
            catch (IOException)
            {
                records = [];
                error = new("Standard input could not be read.", sourceOrigin);
                return false;
            }
        }
        else
        {
            if (!TryResolvePath(source, workingDirectory, out var listPath, out var pathError))
            {
                records = [];
                error = new(pathError, new(source));
                return false;
            }

            sourceOrigin = new(listPath);
            try
            {
                var bytes = File.ReadAllBytes(listPath);
                content = new UTF8Encoding(false, true).GetString(bytes);
                if (content.Length > 0 && content[0] == '\uFEFF')
                {
                    content = content[1..];
                }
            }
            catch (DecoderFallbackException)
            {
                records = [];
                error = new("The input list is not valid UTF-8.", sourceOrigin);
                return false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                records = [];
                error = new("The input list could not be read.", sourceOrigin);
                return false;
            }
        }

        List<InputRecord> result = [];
        using var reader = new StringReader(content);
        var recordNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            recordNumber++;
            if (line.Length > 0)
            {
                result.Add(new(line, sourceOrigin with
                {
                    RecordNumber = recordNumber
                }));
            }
        }

        records = result;
        error = null;
        return true;
    }

    private static Boolean TryResolvePath(String path,
                                          String workingDirectory,
                                          [NotNullWhen(true)] out String? fullPath,
                                          [NotNullWhen(false)] out String? error)
    {
        try
        {
            fullPath = Path.GetFullPath(path, workingDirectory);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = null;
            error = "The input path is invalid.";
            return false;
        }
    }

    private sealed record InputRecord(String Path, UploadSelectionOrigin Origin);
}
