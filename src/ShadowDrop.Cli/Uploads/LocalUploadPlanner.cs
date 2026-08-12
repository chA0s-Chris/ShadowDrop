// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Uploads.Progress;
using ShadowDrop.Crypto;
using System.Collections.Immutable;

/// <summary>
/// Materializes and validates every locally knowable property of an upload batch before configuration or
/// remote work begins.
/// </summary>
internal static class LocalUploadPlanner
{
    internal const Int32 ChunkSize = 1024 * 1024;

    private static readonly StringComparer SourcePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static LocalUploadPlanningResult Create(IEnumerable<UploadSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var materialized = selections.ToImmutableArray();
        var duplicateErrors = FindDuplicates(materialized);
        if (!duplicateErrors.IsEmpty)
        {
            return new(null, duplicateErrors);
        }

        var files = ImmutableArray.CreateBuilder<LocalUploadFile>(materialized.Length);
        var errors = ImmutableArray.CreateBuilder<LocalUploadPlanningError>();

        for (var index = 0; index < materialized.Length; index++)
        {
            var selection = materialized[index];
            var file = selection.File;
            var fileNumber = index + 1;
            try
            {
                file.Refresh();
                if (!file.Exists)
                {
                    errors.Add(new(file, fileNumber, "File is missing."));
                    continue;
                }

                var plaintextLength = file.Length;
                if (plaintextLength <= 0)
                {
                    errors.Add(new(file, fileNumber, "File is empty."));
                    continue;
                }

                using var probe = file.OpenRead();
                _ = probe.Length;

                var chunkCount = checked(((plaintextLength - 1) / ChunkSize) + 1);
                var encryptedLength = checked(plaintextLength + (chunkCount * EncryptedChunk.AuthenticationTagLength));
                files.Add(new(file,
                              selection.Origin,
                              selection.DirectoryRelativePath,
                              fileNumber,
                              plaintextLength,
                              chunkCount,
                              encryptedLength));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                errors.Add(new(file, fileNumber, "File is unreadable."));
            }
            catch (OverflowException)
            {
                errors.Add(new(file, fileNumber, $"{file.Name} exceeds the maximum upload size."));
            }
        }

        return errors.Count > 0
            ? new(null, errors.ToImmutable())
            : new(new(files.ToImmutable()), []);
    }

    public static async Task<UploadExecutionResult> ReportFailureAsync(LocalUploadPlanningResult result,
                                                                       Int32 selectedFileCount,
                                                                       IUploadProgressReporter progressReporter,
                                                                       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(progressReporter);

        List<UploadFileExecutionResult> failures = [];
        foreach (var error in result.Errors)
        {
            await progressReporter.ReportFileFailureAsync(new(error.File.Name,
                                                              error.FileNumber,
                                                              selectedFileCount,
                                                              error.EncryptedLength ?? 0),
                                                          error.Message,
                                                          cancellationToken);
            failures.Add(new(error.File, error.FileNumber, null, error.Message, error.EncryptedLength));
        }

        return new(failures, null, false);
    }

    private static ImmutableArray<LocalUploadPlanningError> FindDuplicates(ImmutableArray<UploadSelection> selections)
    {
        HashSet<String> paths = new(SourcePathComparer);
        var errors = ImmutableArray.CreateBuilder<LocalUploadPlanningError>();

        for (var index = 0; index < selections.Length; index++)
        {
            var file = selections[index].File;
            String fullPath;
            try
            {
                fullPath = Path.GetFullPath(file.FullName);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add(new(file, index + 1, "File path is invalid."));
                continue;
            }

            if (!paths.Add(fullPath))
            {
                errors.Add(new(file, index + 1, "File was selected more than once."));
            }
        }

        return errors.ToImmutable();
    }
}
