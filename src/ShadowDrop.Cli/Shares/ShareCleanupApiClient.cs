// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

internal sealed class ShareCleanupApiClient(HttpClient httpClient)
{
    public async Task<ShareCleanupResultContract> CleanupAsync(Uri serverUrl, String uploadToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serverUrl, "/api/admin/shares/cleanup"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken);

        using var deadline = new ControlPlaneTimeout(cancellationToken);
        try
        {
            using var response = await httpClient.SendAsync(request, deadline.Token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var content = await response.Content.ReadAsStringAsync(deadline.Token);
                    return JsonSerializer.Deserialize(content, CliJsonSerializerContext.Default.ShareCleanupResultContract)
                           ?? throw new ShareCleanupCommandException("Share cleanup failed.");
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new ShareCleanupCommandException("Authentication token invalid or missing.");
                case HttpStatusCode.NotFound:
                    throw new ShareCleanupCommandException("Share cleanup endpoint not found.");
                default:
                    throw new ShareCleanupCommandException("Share cleanup failed.");
            }
        }
        catch (HttpRequestException exception)
        {
            throw new ShareCleanupCommandException("Server connection failed.", exception);
        }
        catch (JsonException exception)
        {
            throw new ShareCleanupCommandException("Share cleanup failed.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShareCleanupCommandException("Server connection failed.", exception);
        }
    }
}
