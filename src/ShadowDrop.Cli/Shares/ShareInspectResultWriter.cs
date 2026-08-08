// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Downloads.Progress;
using ShadowDrop.Contracts;
using System.Globalization;
using System.Text.Json;

internal static class ShareInspectResultWriter
{
    public static async Task WriteAsync(ShareInspectionContract inspection, Boolean json, TextWriter standardOut)
    {
        if (json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(
                                                 inspection,
                                                 OperationalStatusJsonSerializerContext.Default.ShareInspectionContract));
            return;
        }

        await standardOut.WriteLineAsync($"share:{inspection.ShareId}");
        await standardOut.WriteLineAsync($"created:{Timestamp(inspection.CreatedAtUtc)}");
        await standardOut.WriteLineAsync($"expires:{Timestamp(inspection.ExpiresAtUtc)}");
        await standardOut.WriteLineAsync($"revoked:{Timestamp(inspection.RevokedAtUtc)}");
        await standardOut.WriteLineAsync($"statuses:{Values(inspection.Statuses)}");
        await standardOut.WriteLineAsync($"cleanup:{inspection.CleanupState}");
        await standardOut.WriteLineAsync($"cleanup-attempt:{Timestamp(inspection.LastCleanupAttemptAtUtc)}");
        await standardOut.WriteLineAsync($"cleanup-failures:{Values(inspection.CleanupFailureCategories)}");
        await standardOut.WriteLineAsync($"files:{inspection.FileCount.ToString(CultureInfo.InvariantCulture)}");
        await standardOut.WriteLineAsync($"ciphertext-bytes:{inspection.CiphertextBytes.ToString(CultureInfo.InvariantCulture)}");
        foreach (var file in inspection.Files)
        {
            await standardOut.WriteLineAsync(
                $"file:{file.FileId} ciphertext-bytes={file.CiphertextBytes.ToString(CultureInfo.InvariantCulture)} "
                + $"retention={file.RetentionState} original-filename={Filename(file.OriginalFilename)} "
                + $"display-name={Filename(file.DisplayName)}");
        }
    }

    private static String Filename(String? value) => value is null ? "null" : DisplayText.SingleLine(value);

    private static String Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-";

    private static String Values(IReadOnlyList<String> values) => values.Count == 0 ? "-" : String.Join(',', values);
}
