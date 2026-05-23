// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Crypto;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

internal sealed class UploadApiClient(HttpClient httpClient)
{
    private const Int32 MaxAttempts = 3;

    public async Task<Guid> ReserveFileIdAsync(Uri serverUrl, String uploadToken, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(() =>
        {
            var request = CreateRequest(HttpMethod.Post, new Uri(serverUrl, "/api/admin/uploads/reservations"), uploadToken);
            return request;
        }, cancellationToken);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new UploadCommandException("Authentication token invalid or missing.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw CreateUploadFailure(response.StatusCode);
            }

            try
            {
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var reservation =
                    await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.UploadReservationResponse, cancellationToken);
                if (reservation is null || reservation.FileId == Guid.Empty)
                {
                    throw new UploadCommandException("Upload failed; please verify file and try again.");
                }

                return reservation.FileId;
            }
            catch (JsonException)
            {
                throw new UploadCommandException("Upload failed; please verify file and try again.");
            }
        }
    }

    public async Task<Guid> UploadAsync(Uri serverUrl, String uploadToken, UploadFilePlan plan, ShareSecret shareSecret, CancellationToken cancellationToken)
    {
        var response = await SendWithRetryAsync(() =>
        {
            var request = CreateRequest(HttpMethod.Post, new Uri(serverUrl, "/api/admin/uploads"), uploadToken);
            request.Content = CreateMultipartContent(plan, shareSecret, cancellationToken);
            return request;
        }, cancellationToken);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new UploadCommandException("Authentication token invalid or missing.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                throw new UploadCommandException("Upload failed; please verify file and try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw CreateUploadFailure(response.StatusCode);
            }

            try
            {
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var uploadResponse = await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.UploadResponse, cancellationToken);
                if (uploadResponse is null || uploadResponse.FileId == Guid.Empty)
                {
                    throw new UploadCommandException("Upload failed; please verify file and try again.");
                }

                return uploadResponse.FileId;
            }
            catch (JsonException)
            {
                throw new UploadCommandException("Upload failed; please verify file and try again.");
            }
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri requestUri, String uploadToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", uploadToken);
        return request;
    }

    private static UploadCommandException CreateUploadFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => new("Upload failed; please verify file and try again."),
        HttpStatusCode.RequestEntityTooLarge => new("Upload failed; please verify file and try again."),
        _ => new("Server connection failed.")
    };

    private static TimeSpan GetDelay(Int32 attempt) => TimeSpan.FromMilliseconds(200 * attempt);

    private static Boolean IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable;

    private MultipartFormDataContent CreateMultipartContent(UploadFilePlan plan, ShareSecret shareSecret, CancellationToken cancellationToken)
    {
        var multipartContent = new MultipartFormDataContent();
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(plan.Metadata, CliJsonSerializerContext.Default.UploadMetadataPayload);
        var metadataContent = new ByteArrayContent(metadataBytes);
        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipartContent.Add(metadataContent, "metadata");

        var encryptedContent =
            new EncryptedFileContent(plan.File, shareSecret, plan.EncryptionContext, plan.ChunkSize, plan.Metadata.EncryptedLength, cancellationToken);
        encryptedContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(encryptedContent, "content", "cipher.bin");
        return multipartContent;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((attempt < MaxAttempts) && IsTransientStatus(response.StatusCode))
                {
                    response.Dispose();
                    await Task.Delay(GetDelay(attempt), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(GetDelay(attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                await Task.Delay(GetDelay(attempt), cancellationToken);
            }
        }

        throw new UploadCommandException("Server connection failed.");
    }
}
