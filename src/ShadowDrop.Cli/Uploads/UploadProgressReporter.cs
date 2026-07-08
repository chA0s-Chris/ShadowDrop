// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

/// <summary>
/// Writes per-file upload progress and failures to standard error, shared by <c>upload</c> and <c>upload raw</c>.
/// </summary>
internal static class UploadProgressReporter
{
    public static async Task ReportAsync(TextWriter standardError, UploadExecutionResult uploadResult, Int32 requestedFileCount)
    {
        if (uploadResult.BatchErrorMessage is not null)
        {
            await standardError.WriteLineAsync(uploadResult.BatchErrorMessage);
        }

        for (var index = 0; index < uploadResult.Files.Count; index++)
        {
            var fileResult = uploadResult.Files[index];
            if (fileResult.UploadedFileId is not null)
            {
                await standardError.WriteLineAsync($"Uploaded file {fileResult.FileNumber} of {requestedFileCount}.");
            }
            else
            {
                await standardError.WriteLineAsync($"File {fileResult.FileNumber} failed: {fileResult.ErrorMessage}");
            }
        }
    }
}
