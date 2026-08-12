// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Results;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal static class UploadDryRunResultWriter
{
    public static Task WriteJsonAsync(TextWriter standardOut, UploadDryRunResult result) =>
        standardOut.WriteLineAsync(JsonSerializer.Serialize(result, CliJsonSerializerContext.Default.UploadDryRunResult));

    public static async Task WritePlainAsync(TextWriter standardOut, TextWriter standardError, UploadDryRunResult result)
    {
        await standardOut.WriteLineAsync($"dry-run-status:{result.Status}");
        foreach (var file in result.Files)
        {
            await standardOut.WriteLineAsync($"file:{file.SourcePath}");
            await standardOut.WriteLineAsync($"plaintext-bytes:{file.PlaintextBytes}");
            await standardOut.WriteLineAsync($"encrypted-bytes:{file.EncryptedBytes}");
            if (file.QueueDestination is not null)
            {
                await standardOut.WriteLineAsync($"queue-destination:{file.QueueDestination}");
            }
        }

        await standardOut.WriteLineAsync($"selected-files:{result.Totals.SelectedFiles}");
        await standardOut.WriteLineAsync($"excluded-files:{result.Totals.ExcludedFiles}");
        await standardOut.WriteLineAsync($"total-plaintext-bytes:{result.Totals.PlaintextBytes}");
        await standardOut.WriteLineAsync($"total-encrypted-bytes:{result.Totals.EncryptedBytes}");
        if (result.IntendedOutputs.QueueFile is not null)
        {
            await standardOut.WriteLineAsync($"intended-queue-file:{result.IntendedOutputs.QueueFile}");
        }

        if (result.IntendedOutputs.SecretsFile is not null)
        {
            await standardOut.WriteLineAsync($"intended-secrets-file:{result.IntendedOutputs.SecretsFile}");
        }

        foreach (var validation in result.UncheckedValidations)
        {
            await standardOut.WriteLineAsync($"unchecked-validation:{validation}");
        }

        foreach (var error in result.Errors)
        {
            var origin = error.Source switch
            {
                null => String.Empty,
                _ when error.RecordNumber is { } recordNumber => $" Source: {error.Source}, record {recordNumber}.",
                _ => $" Source: {error.Source}."
            };
            await standardError.WriteLineAsync(error.Message + origin);
        }
    }
}
