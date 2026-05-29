// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

internal static class ShareDownloadUriFactory
{
    public static Uri CreateFileUri(Uri serverUrl, String shareId, Guid fileId) =>
        CreateRelativeUri(serverUrl, $"d/{Uri.EscapeDataString(shareId)}/files/{fileId:D}");

    public static Uri CreateManifestUri(Uri serverUrl, String shareId) =>
        CreateRelativeUri(serverUrl, $"d/{Uri.EscapeDataString(shareId)}");

    private static Uri CreateRelativeUri(Uri serverUrl, String relativePath) => new(EnsureDirectoryUri(serverUrl), relativePath);

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
