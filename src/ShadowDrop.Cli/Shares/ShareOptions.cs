// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Uploads;

/// <summary>
/// Validates the share options shared by the end-to-end <c>upload</c> command and the lower-level
/// <c>share create</c> command.
/// </summary>
internal static class ShareOptions
{
    /// <summary>
    /// Validates the expiration and mode/token combination before any I/O begins.
    /// </summary>
    /// <param name="expiresIn">The raw <c>--expires-in</c> value, or <see langword="null"/> for the default.</param>
    /// <param name="directHttp">Whether direct-HTTP mode was requested.</param>
    /// <param name="generateDownloadToken">Whether a download bearer token was requested.</param>
    /// <param name="expiration">The resolved expiration duration.</param>
    /// <param name="error">A user-facing error message when validation fails.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    public static Boolean TryValidate(String? expiresIn,
                                      Boolean directHttp,
                                      Boolean generateDownloadToken,
                                      out TimeSpan expiration,
                                      out String? error)
    {
        expiration = ShareExpiration.Default;
        error = null;

        if (expiresIn is not null && !ShareExpiration.TryParse(expiresIn, out expiration))
        {
            error = "Share expiration invalid. Use a value like 7d, 12h, or 30m.";
            return false;
        }

        if (directHttp && generateDownloadToken)
        {
            error = "Direct HTTP shares cannot generate a download bearer token.";
            return false;
        }

        return true;
    }
}
