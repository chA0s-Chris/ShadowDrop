// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Diagnostics.CodeAnalysis;

internal sealed record UploadCommandOptions(
    FileInfo[] Files,
    String? ServerUrlOverride,
    String? UploadTokenOverride,
    String? ExpiresIn,
    Boolean DirectHttp,
    Boolean GenerateDownloadToken,
    FileInfo? SecretsOut,
    FileInfo? QueueOut,
    Boolean EmbedSecrets,
    Boolean Json,
    Boolean Force,
    String? DisplayName,
    String[] DisplayNameMappings,
    String? InputRoot = null,
    Boolean Flatten = false,
    String? WorkingDirectory = null,
    String[]? InputPaths = null,
    Boolean Recursive = false,
    String[]? IncludePatterns = null,
    String[]? ExcludePatterns = null,
    String[]? FilesFrom = null);

internal static class UploadCommandOptionsValidator
{
    public static Boolean TryValidateLocalOptionCombinations(UploadCommandOptions options,
                                                             [NotNullWhen(false)] out String? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!UploadInputOptionsValidator.TryValidate(options.Recursive,
                                                     options.IncludePatterns,
                                                     options.ExcludePatterns,
                                                     options.FilesFrom,
                                                     out error))
        {
            return false;
        }

        if (!TryValidateQueueDestinationOptions(options, out error))
        {
            return false;
        }

        if (options is { DirectHttp: true, QueueOut: not null })
        {
            error = "Direct HTTP shares do not support queue generation (--queue-out).";
            return false;
        }

        if (options is { DirectHttp: true, SecretsOut: not null })
        {
            error = "Direct HTTP shares do not support writing secrets to a separate file (--secrets-out).";
            return false;
        }

        if (options is { EmbedSecrets: true, QueueOut: null })
        {
            error = "--embed-secrets requires --queue-out.";
            return false;
        }

        error = null;
        return true;
    }

    private static Boolean TryValidateQueueDestinationOptions(UploadCommandOptions options,
                                                              [NotNullWhen(false)] out String? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueueOut is null)
        {
            if (options.InputRoot is not null)
            {
                error = "The --input-root option requires --queue-out.";
                return false;
            }

            if (options.Flatten)
            {
                error = "The --flatten option requires --queue-out.";
                return false;
            }
        }

        if (options.InputRoot is not null && options.Flatten)
        {
            error = "The --input-root and --flatten options cannot be combined.";
            return false;
        }

        error = null;
        return true;
    }
}

internal static class UploadInputOptionsValidator
{
    /// <summary>
    /// Validates the repeatable selection options. A <see langword="null"/> list means the option was omitted; an
    /// empty list means it was supplied without a value, which is rejected rather than read as "no filter".
    /// </summary>
    public static Boolean TryValidate(Boolean recursive,
                                      IReadOnlyList<String>? includePatterns,
                                      IReadOnlyList<String>? excludePatterns,
                                      IReadOnlyList<String>? filesFrom,
                                      [NotNullWhen(false)] out String? error)
    {
        if (includePatterns is { Count: 0 })
        {
            error = "The --include option requires a glob pattern.";
            return false;
        }

        if (excludePatterns is { Count: 0 })
        {
            error = "The --exclude option requires a glob pattern.";
            return false;
        }

        if (filesFrom is { Count: 0 })
        {
            error = "The --files-from option requires a file path or '-'.";
            return false;
        }

        if (!recursive && includePatterns is { Count: > 0 })
        {
            error = "The --include option requires --recursive.";
            return false;
        }

        if (!recursive && excludePatterns is { Count: > 0 })
        {
            error = "The --exclude option requires --recursive.";
            return false;
        }

        error = null;
        return true;
    }
}
