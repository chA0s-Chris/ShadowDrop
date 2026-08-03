// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Http;
using ShadowDrop.Contracts;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

internal sealed class ShareListApiClient(HttpClient httpClient)
{
    public async Task<ShareListPageContract> ListAsync(
        Uri serverUrl,
        String adminToken,
        IReadOnlyList<String> statuses,
        Int32? pageSize,
        String? cursor,
        CancellationToken cancellationToken)
    {
        var query = new StringBuilder();
        foreach (var status in statuses)
        {
            Append(query, "status", status);
        }

        if (pageSize is not null)
        {
            Append(query, "pageSize", pageSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (cursor is not null)
        {
            Append(query, "cursor", cursor);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(serverUrl, $"/api/admin/shares{query}"));
        request.Headers.Authorization = new("Bearer", adminToken);
        using var deadline = new ControlPlaneTimeout(cancellationToken);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ShareListCommandException("Share listing failed.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            var page = JsonSerializer.Deserialize(bytes, OperationalStatusJsonSerializerContext.Default.ShareListPageContract)
                       ?? throw new ShareListCommandException("Share listing failed.");
            if (page.ProtocolVersion != OperationalStatusProtocol.CurrentVersion)
            {
                throw new ShareListCommandException("Share listing failed.");
            }

            return page;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            throw new ShareListCommandException("Share listing failed.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShareListCommandException("Share listing failed.", exception);
        }
    }

    private static void Append(StringBuilder query, String name, String value)
    {
        query.Append(query.Length == 0 ? '?' : '&')
             .Append(name)
             .Append('=')
             .Append(Uri.EscapeDataString(value));
    }
}
