// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Downloads.Progress;
using System.Globalization;

/// <summary>
/// Shared plain-text rendering for credential projections. The free-form name is always emitted as the last
/// field of a line so embedded spaces cannot break the preceding fixed fields.
/// </summary>
internal static class TokenOutput
{
    public static String FormatListLine(UploadCredentialCliProjection credential) =>
        $"credential:{credential.CredentialId} created={Timestamp(credential.CreatedAtUtc)} expires={Timestamp(credential.ExpiresAtUtc)} "
        + $"revoked={Timestamp(credential.RevokedAtUtc)} last-used={Timestamp(credential.LastUsedAtUtc)} "
        + $"capability={credential.Capability} max-file-bytes={Number(credential.MaxEncryptedFileBytes)} "
        + $"max-share-bytes={Number(credential.MaxEncryptedShareBytes)} name={DisplayText.SingleLine(credential.Name)}";

    public static async Task WriteDetailsAsync(TextWriter standardOut, UploadCredentialCliProjection credential)
    {
        await standardOut.WriteLineAsync($"credential-id:{credential.CredentialId}");
        await standardOut.WriteLineAsync($"name:{DisplayText.SingleLine(credential.Name)}");
        await standardOut.WriteLineAsync($"created:{Timestamp(credential.CreatedAtUtc)}");
        await standardOut.WriteLineAsync($"expires:{Timestamp(credential.ExpiresAtUtc)}");
        await standardOut.WriteLineAsync($"revoked:{Timestamp(credential.RevokedAtUtc)}");
        await standardOut.WriteLineAsync($"last-used:{Timestamp(credential.LastUsedAtUtc)}");
        await standardOut.WriteLineAsync($"capability:{credential.Capability}");
        await standardOut.WriteLineAsync($"max-file-bytes:{Number(credential.MaxEncryptedFileBytes)}");
        await standardOut.WriteLineAsync($"max-share-bytes:{Number(credential.MaxEncryptedShareBytes)}");
    }

    private static String Number(Int64? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static String Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "-";
}
