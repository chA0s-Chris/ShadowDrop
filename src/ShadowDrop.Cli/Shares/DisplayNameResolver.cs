// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Contracts;

/// <summary>
/// Resolves recipient-facing display-name overrides from the script-friendly CLI option contract into a mapping
/// keyed by a stable identifier (file full path for <c>upload</c>, file id for <c>share create</c>). All validation
/// happens before any share is created so ambiguous or malformed input fails fast with a clear error. Display names
/// are normalized through <see cref="DisplayNameNormalizer"/> so the CLI and the API agree on the resolved value.
/// </summary>
internal static class DisplayNameResolver
{
    private static readonly IReadOnlyDictionary<Guid, String> EmptyGuidMap = new Dictionary<Guid, String>();

    private static readonly StringComparer FilePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly IReadOnlyDictionary<String, String> EmptyStringMap = new Dictionary<String, String>(FilePathComparer);


    private static readonly StringComparison FilePathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Resolves display names for the lower-level <c>share create</c> workflow.
    /// </summary>
    /// <param name="fileIds">The previously uploaded file ids selected for the share, in command order.</param>
    /// <param name="mappings">The repeated <c>--display-name &lt;file-id&gt;=&lt;name&gt;</c> values.</param>
    /// <param name="overridesByFileId">The resolved overrides keyed by file id.</param>
    /// <param name="error">The validation error when resolution fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when resolution succeeds; otherwise <see langword="false"/>.</returns>
    public static Boolean TryResolveForShareCreate(IReadOnlyList<Guid> fileIds,
                                                   IReadOnlyList<String> mappings,
                                                   out IReadOnlyDictionary<Guid, String> overridesByFileId,
                                                   out String? error)
    {
        if (!TryParseMappings(mappings, out var parsed, out error))
        {
            overridesByFileId = EmptyGuidMap;
            return false;
        }

        var selected = new HashSet<Guid>(fileIds);
        var result = new Dictionary<Guid, String>();
        foreach (var (key, value) in parsed)
        {
            if (!Guid.TryParse(key, out var fileId) || fileId == Guid.Empty)
            {
                overridesByFileId = EmptyGuidMap;
                error = $"The --display-name mapping key '{key}' is not a valid file id.";
                return false;
            }

            if (!selected.Contains(fileId))
            {
                overridesByFileId = EmptyGuidMap;
                error = $"No file id matches the --display-name mapping '{key}'.";
                return false;
            }

            if (!result.TryAdd(fileId, value))
            {
                overridesByFileId = EmptyGuidMap;
                error = $"Duplicate --display-name mapping for file id '{key}'.";
                return false;
            }
        }

        overridesByFileId = result;
        error = null;
        return true;
    }

    /// <summary>
    /// Resolves display names for the end-to-end <c>upload</c> workflow.
    /// </summary>
    /// <param name="files">The files selected for upload, in command order.</param>
    /// <param name="name">The single-file <c>--name</c> value, or <see langword="null"/> when not supplied.</param>
    /// <param name="mappings">The repeated <c>--display-name &lt;path&gt;=&lt;name&gt;</c> values.</param>
    /// <param name="overridesByFullPath">The resolved overrides keyed by <see cref="FileSystemInfo.FullName"/>.</param>
    /// <param name="error">The validation error when resolution fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when resolution succeeds; otherwise <see langword="false"/>.</returns>
    public static Boolean TryResolveForUpload(IReadOnlyList<FileInfo> files,
                                              String? name,
                                              IReadOnlyList<String> mappings,
                                              out IReadOnlyDictionary<String, String> overridesByFullPath,
                                              out String? error)
    {
        if (name is not null)
        {
            if (mappings.Count > 0)
            {
                return Fail("The --name and --display-name options cannot be combined.", out overridesByFullPath, out error);
            }

            if (files.Count != 1)
            {
                return Fail("The --name option requires exactly one file. Use --display-name <path>=<name> for multiple files.",
                            out overridesByFullPath, out error);
            }

            var normalized = DisplayNameNormalizer.Normalize(name);
            if (normalized is null)
            {
                return Fail("The display name provided to --name is empty.", out overridesByFullPath, out error);
            }

            overridesByFullPath = new Dictionary<String, String>(FilePathComparer)
            {
                [files[0].FullName] = normalized
            };
            error = null;
            return true;
        }

        if (!TryParseMappings(mappings, out var parsed, out error))
        {
            overridesByFullPath = EmptyStringMap;
            return false;
        }

        var result = new Dictionary<String, String>(FilePathComparer);
        foreach (var (key, value) in parsed)
        {
            String fullPath;
            try
            {
                fullPath = Path.GetFullPath(key);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Fail($"No file matches the --display-name mapping '{key}'.", out overridesByFullPath, out error);
            }

            var matches = files.Count(file => String.Equals(file.FullName, fullPath, FilePathComparison));
            if (matches == 0)
            {
                return Fail($"No file matches the --display-name mapping '{key}'.", out overridesByFullPath, out error);
            }

            if (matches > 1)
            {
                return Fail($"The --display-name mapping '{key}' matches more than one file.", out overridesByFullPath, out error);
            }

            if (!result.TryAdd(fullPath, value))
            {
                return Fail($"Multiple --display-name mappings resolve to the same file '{key}'.", out overridesByFullPath, out error);
            }
        }

        overridesByFullPath = result;
        error = null;
        return true;
    }

    private static Boolean Fail(String message, out IReadOnlyDictionary<String, String> overrides, out String? error)
    {
        overrides = EmptyStringMap;
        error = message;
        return false;
    }

    /// <summary>
    /// Splits raw <c>key=value</c> mappings, normalizing each value and rejecting malformed entries, duplicate keys,
    /// and values that normalize to empty.
    /// </summary>
    private static Boolean TryParseMappings(IReadOnlyList<String> mappings,
                                            out IReadOnlyList<KeyValuePair<String, String>> parsed,
                                            out String? error)
    {
        var result = new List<KeyValuePair<String, String>>(mappings.Count);
        var seenKeys = new HashSet<String>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var separatorIndex = mapping.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                parsed = result;
                error = $"Invalid display-name mapping '{mapping}'. Use --display-name <path-or-file-id>=<name>.";
                return false;
            }

            var key = mapping[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                parsed = result;
                error = $"Invalid display-name mapping '{mapping}'. Use --display-name <path-or-file-id>=<name>.";
                return false;
            }

            var value = DisplayNameNormalizer.Normalize(mapping[(separatorIndex + 1)..]);
            if (value is null)
            {
                parsed = result;
                error = $"The display name for '{key}' is empty.";
                return false;
            }

            if (!seenKeys.Add(key))
            {
                parsed = result;
                error = $"Duplicate --display-name mapping for '{key}'.";
                return false;
            }

            result.Add(new(key, value));
        }

        parsed = result;
        error = null;
        return true;
    }
}
