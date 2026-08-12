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
    String? WorkingDirectory = null);

internal static class UploadCommandOptionsValidator
{
    public static Boolean TryValidateLocalOptionCombinations(UploadCommandOptions options,
                                                             [NotNullWhen(false)] out String? error)
    {
        ArgumentNullException.ThrowIfNull(options);

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

    public static Boolean TryValidateQueueDestinationOptions(UploadCommandOptions options,
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
