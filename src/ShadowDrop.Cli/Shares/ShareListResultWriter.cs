// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Contracts;
using System.Globalization;
using System.Text.Json;

internal static class ShareListResultWriter
{
    public static async Task WriteAsync(ShareListPageContract page, Boolean json, TextWriter standardOut)
    {
        if (json)
        {
            await standardOut.WriteLineAsync(JsonSerializer.Serialize(page,
                                                                      OperationalStatusJsonSerializerContext.Default.ShareListPageContract));
            return;
        }

        await standardOut.WriteLineAsync($"total-matching:{page.TotalMatching.ToString(CultureInfo.InvariantCulture)}");
        foreach (var item in page.Items)
        {
            await standardOut.WriteLineAsync(
                $"share:{item.ShareId} created={Timestamp(item.CreatedAtUtc)} expires={Timestamp(item.ExpiresAtUtc)} "
                + $"revoked={Timestamp(item.RevokedAtUtc)} statuses={Values(item.Statuses)} cleanup={item.CleanupState} "
                + $"cleanup-attempt={Timestamp(item.LastCleanupAttemptAtUtc)} cleanup-failures={Values(item.CleanupFailureCategories)} "
                + $"files={item.FileCount.ToString(CultureInfo.InvariantCulture)} "
                + $"ciphertext-bytes={item.CiphertextBytes.ToString(CultureInfo.InvariantCulture)}");
        }

        await standardOut.WriteLineAsync($"next-cursor:{page.NextCursor ?? "-"}");
    }

    private static String Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-";

    private static String Values(IReadOnlyList<String> values) => values.Count == 0 ? "-" : String.Join(',', values);
}
