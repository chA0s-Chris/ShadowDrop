// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Http;
using ShadowDrop.Cli.Uploads.Progress;
using ShadowDrop.Crypto;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

internal sealed class UploadApiClient(
    HttpClient httpClient,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
    TimeProvider? timeProvider = null)
{
    private const Int32 MaxAttempts = 3;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? Task.Delay;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<UploadCapabilitiesResponse> GetCapabilitiesAsync(Uri serverUrl, String uploadToken, CancellationToken cancellationToken)
        => await SendWithRetryAsync(
            (_, _) => CreateRequest(HttpMethod.Get, new Uri(serverUrl, "/api/uploads/capabilities"), uploadToken),
            static async (response, responseCancellation) =>
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new UploadCommandException("Authentication token invalid or missing.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateCapabilitiesFailure();
                }

                try
                {
                    await using var contentStream = await response.Content.ReadAsStreamAsync(responseCancellation);
                    var capabilities =
                        await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.UploadCapabilitiesResponse, responseCancellation);
                    if (capabilities is null || capabilities.MaxFilePayloadBytes <= 0)
                    {
                        throw CreateCapabilitiesFailure();
                    }

                    return capabilities;
                }
                catch (JsonException)
                {
                    throw CreateCapabilitiesFailure();
                }
            },
            cancellationToken,
            false);

    public async Task<Guid> ReserveFileIdAsync(Uri serverUrl, String uploadToken, CancellationToken cancellationToken)
        => await SendWithRetryAsync(
            (_, _) => CreateRequest(HttpMethod.Post, new Uri(serverUrl, "/api/uploads/reservations"), uploadToken),
            static async (response, responseCancellation) =>
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
                    await using var contentStream = await response.Content.ReadAsStreamAsync(responseCancellation);
                    var reservation =
                        await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.UploadReservationResponse, responseCancellation);
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
            },
            cancellationToken,
            false);

    public async Task<Guid> UploadAsync(Uri serverUrl,
                                        String uploadToken,
                                        UploadFilePlan plan,
                                        ShareSecret shareSecret,
                                        UploadProgressSink? progressSink,
                                        CancellationToken cancellationToken)
        => await SendWithRetryAsync(
            (requestCancellation, reportActivity) =>
            {
                var request = CreateRequest(HttpMethod.Post, new Uri(serverUrl, "/api/uploads"), uploadToken);
                request.Content = CreateMultipartContent(plan, shareSecret, progressSink?.Bytes, requestCancellation, reportActivity);
                return request;
            },
            static async (response, responseCancellation) =>
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
                    await using var contentStream = await response.Content.ReadAsStreamAsync(responseCancellation);
                    var uploadResponse =
                        await JsonSerializer.DeserializeAsync(contentStream, CliJsonSerializerContext.Default.UploadResponse, responseCancellation);
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
            },
            cancellationToken,
            true,
            progressSink);

    private static UploadCommandException CreateCapabilitiesFailure() =>
        new("Upload limit could not be resolved from the server; upgrade the server or verify connectivity.");

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

    private static TimeSpan GetDelay(Int32 attempt) => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));

    private static Boolean IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.ServiceUnavailable;

    private static async Task ReportRetryAsync(UploadProgressSink? progressSink, Int32 nextAttempt, CancellationToken cancellationToken)
    {
        if (progressSink is not null)
        {
            await progressSink.RetryingAsync(nextAttempt, cancellationToken);
        }
    }

    private MultipartFormDataContent CreateMultipartContent(UploadFilePlan plan,
                                                            ShareSecret shareSecret,
                                                            IProgress<Int64>? progress,
                                                            CancellationToken cancellationToken,
                                                            Action? reportActivity)
    {
        var multipartContent = new MultipartFormDataContent();
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(plan.Metadata, CliJsonSerializerContext.Default.UploadMetadataPayload);
        var metadataContent = new ByteArrayContent(metadataBytes);
        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipartContent.Add(metadataContent, "metadata");

        var encryptedContent =
            new EncryptedFileContent(plan.File,
                                     shareSecret,
                                     plan.EncryptionContext,
                                     plan.ChunkSize,
                                     plan.Metadata.EncryptedLength,
                                     progress,
                                     cancellationToken,
                                     reportActivity);
        encryptedContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipartContent.Add(encryptedContent, "content", "cipher.bin");
        return multipartContent;
    }

    private async Task<T> SendWithRetryAsync<T>(Func<CancellationToken, Action?, HttpRequestMessage> requestFactory,
                                                Func<HttpResponseMessage, CancellationToken, Task<T>> processResponseAsync,
                                                CancellationToken cancellationToken,
                                                Boolean isStreamingUpload,
                                                UploadProgressSink? progressSink = null)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var uploadTimeout = isStreamingUpload ? new UploadAttemptTimeout(cancellationToken, _timeProvider) : null;
            using var controlTimeout = isStreamingUpload ? null : new ControlPlaneTimeout(cancellationToken, _timeProvider);
            var effectiveCancellation = uploadTimeout?.Token ?? controlTimeout!.Token;
            Action? reportActivity = uploadTimeout is null ? null : uploadTimeout.Reset;
            using var request = requestFactory(effectiveCancellation, reportActivity);
            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveCancellation);
                if ((attempt < MaxAttempts) && IsTransientStatus(response.StatusCode))
                {
                    await ReportRetryAsync(progressSink, attempt + 1, cancellationToken);
                    await _delayAsync(GetDelay(attempt), cancellationToken);
                    continue;
                }

                return await processResponseAsync(response, effectiveCancellation);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await ReportRetryAsync(progressSink, attempt + 1, cancellationToken);
                await _delayAsync(GetDelay(attempt), cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new UploadCommandException("Server connection failed.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                await ReportRetryAsync(progressSink, attempt + 1, cancellationToken);
                await _delayAsync(GetDelay(attempt), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UploadCommandException("Server connection failed.");
            }
        }

        throw new UploadCommandException("Server connection failed.");
    }
}
