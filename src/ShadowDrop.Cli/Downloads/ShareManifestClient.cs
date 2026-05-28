// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Contracts;
using System.Net;
using System.Text.Json;

internal sealed class ShareManifestClient(HttpClient httpClient)
{
    public async Task<ShareManifestContract> GetAsync(Uri serverUrl, String shareId, String? bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(serverUrl, $"/d/{Uri.EscapeDataString(shareId)}"));
        if (!String.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new("Bearer", bearerToken);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
        {
            throw new DownloadCommandException("Share unavailable or unauthorized.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new DownloadCommandException("Download authorization failed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadCommandException("Server connection failed.");
        }

        try
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.ShareManifestContract, cancellationToken);
            if (manifest?.Files is null || manifest.Files.Count == 0)
            {
                throw new DownloadCommandException("Share metadata invalid or missing.");
            }

            return manifest;
        }
        catch (JsonException)
        {
            throw new DownloadCommandException("Share metadata invalid or missing.");
        }
    }
}
