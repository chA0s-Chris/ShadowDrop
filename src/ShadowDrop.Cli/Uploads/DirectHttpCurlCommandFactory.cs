// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using System.Security.Cryptography;

/// <summary>
/// Builds a ready-to-run, logging-safe <c>curl</c> command for a direct HTTP download. The key material is passed via
/// the <see cref="DownloadKeyConstants.HeaderName"/> header instead of the <c>sd-key</c> query parameter, keeping it out
/// of request URLs (and therefore out of access/proxy logs). The direct HTTP endpoint requires exactly one key source,
/// so the file URL deliberately omits <c>sd-key</c>. The emitted command targets POSIX <c>sh</c> only.
/// </summary>
internal static class DirectHttpCurlCommandFactory
{
    public static String Create(Uri serverUrl, String shareToken, Guid fileId, String shareSecretHex, String fileName)
    {
        var keyMaterial = Convert.FromHexString(shareSecretHex);
        try
        {
            var keyMaterialBase64 = Convert.ToBase64String(keyMaterial);
            var fileUri = ShareDownloadUriFactory.CreateFileUri(serverUrl, shareToken, fileId);
            var headerArgument = PosixQuote($"{DownloadKeyConstants.HeaderName}: {keyMaterialBase64}");
            var urlArgument = PosixQuote(fileUri.AbsoluteUri);
            var outputArgument = PosixQuote(fileName);
            return $"curl -H {headerArgument} {urlArgument} -o {outputArgument}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    /// <summary>
    /// Quotes a value for POSIX <c>sh</c> by wrapping it in single quotes and escaping any embedded single quote as the
    /// <c>'\''</c> sequence. Single-quoting avoids shell expansion of <c>$</c>, backticks, and <c>\</c> in file names.
    /// </summary>
    private static String PosixQuote(String value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
