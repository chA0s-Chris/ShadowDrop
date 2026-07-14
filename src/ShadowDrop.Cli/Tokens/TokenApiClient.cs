// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal sealed class TokenApiClient(HttpClient httpClient)
{
    public Task<CreateUploadCredentialCliResult> CreateAsync(Uri serverUrl, String adminToken,
                                                             CreateUploadCredentialCliRequest request,
                                                             CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = JsonSerializer.Serialize(request, CliJsonSerializerContext.Default.CreateUploadCredentialCliRequest);
        return SendAsync(serverUrl,
                         adminToken,
                         HttpMethod.Post,
                         "/api/admin/upload-credentials/",
                         payload,
                         HttpStatusCode.Created,
                         CliJsonSerializerContext.Default.CreateUploadCredentialCliResult,
                         cancellationToken);
    }

    public Task<UploadCredentialCliProjection> InspectAsync(Uri serverUrl, String adminToken, Guid credentialId,
                                                            CancellationToken cancellationToken) =>
        SendAsync(serverUrl,
                  adminToken,
                  HttpMethod.Get,
                  $"/api/admin/upload-credentials/{credentialId}",
                  null,
                  HttpStatusCode.OK,
                  CliJsonSerializerContext.Default.UploadCredentialCliProjection,
                  cancellationToken);

    public Task<UploadCredentialCliListResult> ListAsync(Uri serverUrl, String adminToken, String? cursor, Int32? limit,
                                                         CancellationToken cancellationToken)
    {
        var query = new StringBuilder();
        if (!String.IsNullOrWhiteSpace(cursor))
        {
            query.Append("?cursor=").Append(Uri.EscapeDataString(cursor));
        }

        if (limit is not null)
        {
            query.Append(query.Length == 0 ? '?' : '&').Append("limit=").Append(limit.Value);
        }

        return SendAsync(serverUrl,
                         adminToken,
                         HttpMethod.Get,
                         $"/api/admin/upload-credentials/{query}",
                         null,
                         HttpStatusCode.OK,
                         CliJsonSerializerContext.Default.UploadCredentialCliListResult,
                         cancellationToken);
    }

    public async Task RevokeAsync(Uri serverUrl, String adminToken, Guid credentialId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(serverUrl, adminToken, HttpMethod.Post,
                                          $"/api/admin/upload-credentials/{credentialId}/revoke", null);
        using var deadline = new ControlPlaneTimeout(cancellationToken);
        try
        {
            using var response = await httpClient.SendAsync(request, deadline.Token);
            if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK))
            {
                throw MapError(response.StatusCode);
            }
        }
        catch (HttpRequestException exception)
        {
            throw new TokenCommandException("Server connection failed.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TokenCommandException("Server connection failed.", exception);
        }
    }

    private static HttpRequestMessage CreateRequest(Uri serverUrl, String adminToken, HttpMethod method, String relativeUrl,
                                                    String? requestPayload)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminToken);

        var request = new HttpRequestMessage(method, new Uri(serverUrl, relativeUrl));
        request.Headers.Authorization = new("Bearer", adminToken);
        if (requestPayload is not null)
        {
            request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static TokenCommandException MapError(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new("Admin token invalid or missing."),
        HttpStatusCode.NotFound => new("Upload credential not found."),
        HttpStatusCode.BadRequest => new("Upload credential request invalid."),
        _ => new("Upload credential operation failed.")
    };

    private async Task<TResult> SendAsync<TResult>(Uri serverUrl, String adminToken, HttpMethod method, String relativeUrl,
                                                   String? requestPayload, HttpStatusCode expectedStatusCode,
                                                   JsonTypeInfo<TResult> resultTypeInfo,
                                                   CancellationToken cancellationToken)
        where TResult : class
    {
        using var request = CreateRequest(serverUrl, adminToken, method, relativeUrl, requestPayload);
        using var deadline = new ControlPlaneTimeout(cancellationToken);
        try
        {
            using var response = await httpClient.SendAsync(request, deadline.Token);
            if (response.StatusCode != expectedStatusCode)
            {
                throw MapError(response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync(deadline.Token);
            return JsonSerializer.Deserialize(content, resultTypeInfo)
                   ?? throw new TokenCommandException("Upload credential operation failed.");
        }
        catch (HttpRequestException exception)
        {
            throw new TokenCommandException("Server connection failed.", exception);
        }
        catch (JsonException exception)
        {
            throw new TokenCommandException("Upload credential operation failed.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TokenCommandException("Server connection failed.", exception);
        }
    }
}
