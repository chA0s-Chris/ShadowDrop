// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

internal static class ShareDownloadUriFactory
{
    public static Uri CreateFileUri(Uri serverUrl, String shareToken, Guid fileId) =>
        CreateRelativeUri(serverUrl, $"d/{Uri.EscapeDataString(shareToken)}/files/{fileId:D}");

    public static Uri CreateManifestUri(Uri serverUrl, String shareToken) =>
        CreateRelativeUri(serverUrl, $"d/{Uri.EscapeDataString(shareToken)}");

    internal static Uri NormalizeServerUrl(Uri serverUrl) => EnsureDirectoryUri(serverUrl);

    private static Uri CreateRelativeUri(Uri serverUrl, String relativePath) => new(NormalizeServerUrl(serverUrl), relativePath);

    private static Uri EnsureDirectoryUri(Uri serverUrl)
    {
        if (serverUrl.AbsolutePath.EndsWith('/'))
        {
            return serverUrl;
        }

        var builder = new UriBuilder(serverUrl)
        {
            Path = $"{serverUrl.AbsolutePath}/"
        };
        return builder.Uri;
    }
}
