// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using ShadowDrop.Contracts;
using System.Net;
using System.Text.Json;

internal sealed record ServerStatusApiResponse(HttpStatusCode StatusCode, Object? Status, Int32? ProtocolVersion = null);

internal sealed class ServerStatusApiClient
{
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(12);
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public ServerStatusApiClient(HttpClient httpClient,
                                 TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
    }

    public async Task<ServerStatusApiResponse> GetAsync(
        Uri serverUrl,
        ServerStatusMode mode,
        String? bearerToken,
        CancellationToken cancellationToken)
    {
        using var deadlineCancellation = new CancellationTokenSource(TotalTimeout, _timeProvider);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCancellation.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(serverUrl, ResolvePath(mode)));
        if (!String.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new("Bearer", bearerToken);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCancellation.Token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
        {
            return new(response.StatusCode, null);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable
            && (response.Content.Headers.ContentLength == 0 || (mode == ServerStatusMode.Upload && response.Content.Headers.ContentLength is null)))
        {
            var content = await response.Content.ReadAsByteArrayAsync(linkedCancellation.Token);
            if (content.Length == 0)
            {
                return new(response.StatusCode, null);
            }

            return CreateStatusResponse(response.StatusCode, content, mode);
        }

        if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.ServiceUnavailable)
        {
            return new(response.StatusCode, null);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(linkedCancellation.Token);
        return CreateStatusResponse(response.StatusCode, bytes, mode);
    }

    private static ServerStatusApiResponse CreateStatusResponse(
        HttpStatusCode statusCode,
        ReadOnlyMemory<Byte> content,
        ServerStatusMode mode)
    {
        var protocolVersion = ReadProtocolVersion(content);
        if (!IsCompatible(protocolVersion))
        {
            return new(statusCode, null, protocolVersion);
        }

        return new(statusCode, Deserialize(content.Span, mode), protocolVersion);
    }

    private static Object Deserialize(ReadOnlySpan<Byte> content, ServerStatusMode mode)
    {
        Object status = mode switch
        {
            ServerStatusMode.Public => JsonSerializer.Deserialize(content,
                                                                  OperationalStatusJsonSerializerContext.Default.PublicServerStatusContract)
                                       ?? throw new JsonException("The public status response was empty."),
            ServerStatusMode.Upload => JsonSerializer.Deserialize(content,
                                                                  OperationalStatusJsonSerializerContext.Default.UploadServerStatusContract)
                                       ?? throw new JsonException("The upload status response was empty."),
            ServerStatusMode.Admin => JsonSerializer.Deserialize(content,
                                                                 OperationalStatusJsonSerializerContext.Default.AdminServerStatusContract)
                                      ?? throw new JsonException("The admin status response was empty."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported server status mode.")
        };

        Validate(status);
        return status;
    }

    private static Boolean IsCompatible(Int32 protocolVersion) =>
        protocolVersion is >= OperationalStatusProtocol.MinimumSupportedVersion
                           and <= OperationalStatusProtocol.MaximumSupportedVersion;

    private static Boolean IsKnownReason(String reason) => reason is OperationalStatusReasons.None
                                                                     or OperationalStatusReasons.DependencyTimeout
                                                                     or OperationalStatusReasons.DependencyUnavailable
                                                                     or OperationalStatusReasons.CapabilityDisabled
                                                                     or OperationalStatusReasons.ConfigurationInvalid;

    private static Int32 ReadProtocolVersion(ReadOnlyMemory<Byte> content)
    {
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("protocolVersion", out var property)
            || !property.TryGetInt32(out var protocolVersion)
            || protocolVersion <= 0)
        {
            throw new JsonException("The status response does not contain a valid protocol version.");
        }

        return protocolVersion;
    }

    private static String ResolvePath(ServerStatusMode mode) => mode switch
    {
        ServerStatusMode.Public => "/api/status",
        ServerStatusMode.Upload => "/api/status/upload",
        ServerStatusMode.Admin => "/api/admin/status",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported server status mode.")
    };

    private static void Validate(Object status)
    {
        switch (status)
        {
            case PublicServerStatusContract publicStatus:
                ValidateCommon(publicStatus.ProtocolVersion, publicStatus.Live, publicStatus.Reason);
                break;
            case UploadServerStatusContract uploadStatus:
                ValidateCommon(uploadStatus.ProtocolVersion, uploadStatus.Live, uploadStatus.Reason);
                break;
            case AdminServerStatusContract adminStatus:
                ValidateCommon(adminStatus.ProtocolVersion, adminStatus.Live, adminStatus.Reason);
                if (!IsCompatible(adminStatus.ProtocolVersion))
                {
                    break;
                }

                if (adminStatus.Components.Any(component => component.Name is not ("metadata" or "storage")
                                                            || component.State is not (OperationalComponentStates.Ready
                                                                                       or OperationalComponentStates.NotReady
                                                                                       or OperationalComponentStates.NotApplicable)
                                                            || !IsKnownReason(component.Reason))
                    || adminStatus.Providers.Metadata is not ("litedb" or "mongodb")
                    || adminStatus.Providers.Storage is not ("filesystem" or "mongodb-gridfs" or "s3")
                    || adminStatus.Cleanup.LastOutcome is not ("not-run" or "success" or "partial-failure" or "failure" or "skipped")
                    || adminStatus.ConfigurationWarnings.Any(warning => warning != OperationalStatusWarnings.StorageAccountingIncomplete))
                {
                    throw new JsonException("The admin status response contains an unsupported value.");
                }

                break;
            default:
                throw new JsonException("The status response type was unsupported.");
        }
    }

    private static void ValidateCommon(Int32 protocolVersion, Boolean live, String reason)
    {
        if (protocolVersion <= 0 || !live || (IsCompatible(protocolVersion) && !IsKnownReason(reason)))
        {
            throw new JsonException("The status response contains an invalid common field.");
        }
    }
}
