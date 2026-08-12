// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

internal sealed record UploadInputResolution(
    ImmutableArray<UploadSelection> Selections,
    Int32 ExcludedFileCount,
    ImmutableArray<UploadInputError> Errors)
{
    public Boolean IsValid => Errors.IsEmpty && !Selections.IsEmpty;
}

internal sealed record UploadInputError(String Message, UploadSelectionOrigin Origin);

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
            return new([], 0, [patternError]);
        }

        if (!TryCreateGlobs(excludePatterns, "--exclude", out var excludes, out patternError))
        {
            return new([], 0, [patternError]);
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
                return new([], 0, [listError]);
            }

            records.AddRange(listedRecords);
        }

        List<UploadSelection> selections = [];
        var excluded = 0;
        foreach (var record in records)
        {
            if (!TryResolvePath(record.Path, workingDirectory, out var fullPath, out var pathError))
            {
                return new([], 0, [new(pathError, record.Origin)]);
            }

            if (!Directory.Exists(fullPath))
            {
                selections.Add(new(new(fullPath), record.Origin));
                continue;
            }

            if (!recursive)
            {
                return new([], 0,
                           [new($"The input path '{fullPath}' is a directory. Pass --recursive to include its files.", record.Origin)]);
            }

            if (!TryExpandDirectory(fullPath, record.Origin, includes, excludes, out var expanded, out var directoryExcluded, out var expansionError))
            {
                return new([], 0, [expansionError]);
            }

            if (expanded.Count == 0)
            {
                return new([], 0, [new($"The directory '{fullPath}' did not select any files.", record.Origin)]);
            }

            selections.AddRange(expanded);
            excluded += directoryExcluded;
        }

        return selections.Count == 0
            ? Invalid("No input files were selected.")
            : new([.. selections], excluded, []);
    }

    private static UploadInputResolution Invalid(String message) =>
        new([], 0, [new(message, UploadSelectionOrigin.CommandLine)]);

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
                                              [NotNullWhen(false)] out UploadInputError? error)
    {
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                selections = [];
                excluded = 0;
                error = new($"The directory '{root}' is a link and will not be traversed.", origin);
                return false;
            }

            List<(String FullPath, String RelativePath)> discovered = [];
            Stack<String> pending = new();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
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
                    discovered.Add((entry.FullName, relativePath));
                }
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

                selected.Add(new(new(file.FullPath), origin, file.RelativePath));
            }

            selections = selected;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            selections = [];
            excluded = 0;
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
