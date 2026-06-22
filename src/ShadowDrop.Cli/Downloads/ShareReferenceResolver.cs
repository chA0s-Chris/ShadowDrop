// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

/// <summary>
/// Resolves a user-supplied share reference (a public share token, or a full share URL of the form
/// <c>.../d/&lt;token&gt;</c>) into a server base URL and the bare share token.
/// </summary>
internal static class ShareReferenceResolver
{
    /// <summary>
    /// Attempts to resolve a token-or-URL into a server URL and share token.
    /// </summary>
    /// <param name="shareTokenOrUrl">A bare share token or an absolute share URL.</param>
    /// <param name="serverUrlFallback">The configured server URL used when a bare token is supplied.</param>
    /// <param name="serverUrl">The resolved server base URL.</param>
    /// <param name="shareToken">The resolved bare share token.</param>
    /// <returns><see langword="true"/> when a token could be resolved; <see langword="false"/> for a URL that is not a valid share URL.</returns>
    public static Boolean TryResolve(String shareTokenOrUrl, Uri serverUrlFallback, out Uri serverUrl, out String shareToken)
    {
        if (Uri.TryCreate(shareTokenOrUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            var segments = absolute.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && String.Equals(segments[^2], "d", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = segments.Length == 2 ? "/" : $"/{String.Join('/', segments[..^2])}/";
                var builder = new UriBuilder(absolute)
                {
                    Path = basePath,
                    Query = String.Empty,
                    Fragment = String.Empty
                };
                serverUrl = builder.Uri;
                shareToken = Uri.UnescapeDataString(segments[^1]);
                return true;
            }

            serverUrl = serverUrlFallback;
            shareToken = String.Empty;
            return false;
        }

        serverUrl = serverUrlFallback;
        shareToken = shareTokenOrUrl;
        return true;
    }
}
