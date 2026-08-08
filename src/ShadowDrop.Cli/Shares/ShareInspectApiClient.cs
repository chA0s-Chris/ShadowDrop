// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Http;
using ShadowDrop.Contracts;
using System.Net;
using System.Text.Json;

internal sealed class ShareInspectApiClient
{
    private readonly HttpClient _httpClient;

    public ShareInspectApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ShareInspectionContract> InspectAsync(
        Uri serverUrl,
        String adminToken,
        Guid shareId,
        Boolean includeFilenames,
        CancellationToken cancellationToken)
    {
        var canonicalId = shareId.ToString("D").ToLowerInvariant();
        var query = includeFilenames ? "?includeFilenames=true" : String.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Get,
                                                   new Uri(serverUrl, $"/api/admin/shares/{canonicalId}{query}"));
        request.Headers.Authorization = new("Bearer", adminToken);
        using var deadline = new ControlPlaneTimeout(cancellationToken);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

            // Only the two documented outcomes have a body worth reading; every other status is a generic failure, so it must not
            // buffer an arbitrarily large response into memory first.
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NotFound))
            {
                throw new ShareInspectCommandException();
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var error = JsonSerializer.Deserialize(bytes, OperationalStatusJsonSerializerContext.Default.OperationalErrorContract);
                throw new ShareInspectCommandException(String.Equals(error?.Reason,
                                                                     OperationalErrorReasons.NotFound,
                                                                     StringComparison.Ordinal));
            }

            var inspection = JsonSerializer.Deserialize(bytes,
                                                        OperationalStatusJsonSerializerContext.Default.ShareInspectionContract)
                             ?? throw new ShareInspectCommandException();
            if (inspection.ProtocolVersion != OperationalStatusProtocol.CurrentVersion)
            {
                throw new ShareInspectCommandException();
            }

            return inspection;
        }
        catch (ShareInspectCommandException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            throw new ShareInspectCommandException(innerException: exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShareInspectCommandException(innerException: exception);
        }
    }
}
