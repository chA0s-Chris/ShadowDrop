// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using ShadowDrop.Cli.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

internal sealed class CreateShareApiClient(HttpClient httpClient)
{
    public async Task<CreateShareCliResult> CreateAsync(Uri serverUrl,
                                                        String uploadToken,
                                                        CreateShareCliRequest request,
                                                        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadToken);
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(serverUrl, "/api/admin/shares"))
        {
            Content = JsonContent.Create(request, CliJsonSerializerContext.Default.CreateShareCliRequest)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Created => await ReadResultAsync(response, cancellationToken),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => throw new CreateShareCommandException(
                    "Authentication token invalid or missing."),
                HttpStatusCode.BadRequest => throw new CreateShareCommandException("Invalid share request."),
                _ => throw new CreateShareCommandException("Server connection failed.")
            };
        }
        catch (HttpRequestException exception)
        {
            throw new CreateShareCommandException("Server connection failed.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CreateShareCommandException("Server connection failed.", exception);
        }
    }

    private static async Task<CreateShareCliResult> ReadResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync(CliJsonSerializerContext.Default.CreateShareCliResult, cancellationToken);
            return result ?? throw new CreateShareCommandException("Share creation failed.");
        }
        catch (JsonException exception)
        {
            throw new CreateShareCommandException("Share creation failed.", exception);
        }
    }
}

internal sealed class CreateShareCommandException : Exception
{
    public CreateShareCommandException(String message)
        : base(message) { }

    public CreateShareCommandException(String message, Exception innerException)
        : base(message, innerException) { }
}
