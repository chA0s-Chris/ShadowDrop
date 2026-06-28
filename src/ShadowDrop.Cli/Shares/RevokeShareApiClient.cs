// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using System.Net;
using System.Net.Http.Headers;

internal sealed class RevokeShareApiClient(HttpClient httpClient)
{
    public async Task RevokeAsync(Uri serverUrl, String uploadToken, Guid shareId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadToken);
        if (shareId == Guid.Empty)
        {
            throw new ArgumentException("Share id must not be empty.", nameof(shareId));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serverUrl, $"/api/admin/shares/{shareId}/revoke"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NoContent:
                case HttpStatusCode.OK:
                    return;
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new RevokeShareCommandException("Authentication token invalid or missing.");
                case HttpStatusCode.NotFound:
                    throw new RevokeShareCommandException("Share not found.");
                default:
                    throw new RevokeShareCommandException("Share revocation failed.");
            }
        }
        catch (HttpRequestException exception)
        {
            throw new RevokeShareCommandException("Server connection failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RevokeShareCommandException("Server connection failed.", exception);
        }
    }
}
