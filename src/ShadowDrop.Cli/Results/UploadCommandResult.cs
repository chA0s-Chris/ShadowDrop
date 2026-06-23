// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Results;

using System.Text.Json.Serialization;

/// <summary>
/// Stable, Native-AOT-compatible JSON contract describing the outcome of an end-to-end upload.
/// </summary>
/// <remarks>
/// Credentials are included by default because selecting JSON is itself an output destination. When
/// credentials are redirected to a dedicated file, <see cref="Credentials"/> is omitted and
/// <see cref="SecretsFile"/> carries the file path instead.
/// </remarks>
internal sealed record UploadCommandResult(
    [property: JsonPropertyName("status")] String Status,
    [property: JsonPropertyName("uploadedFileIds")]
    IReadOnlyList<String> UploadedFileIds,
    [property: JsonPropertyName("shareId")]
    Guid? ShareId,
    [property: JsonPropertyName("shareToken")]
    String? ShareToken,
    [property: JsonPropertyName("shareUrl")]
    String? ShareUrl,
    [property: JsonPropertyName("credentials")]
    UploadCredentials? Credentials,
    [property: JsonPropertyName("secretsFile")]
    String? SecretsFile,
    [property: JsonPropertyName("queueFile")]
    String? QueueFile);

/// <summary>
/// The non-retrievable credentials required to download a share.
/// </summary>
internal sealed record UploadCredentials(
    [property: JsonPropertyName("shareKey")]
    String ShareKey,
    [property: JsonPropertyName("downloadBearerToken")]
    String? DownloadBearerToken);

/// <summary>
/// Stable JSON contract for the dedicated credential document written by <c>--secrets-out</c>.
/// </summary>
/// <remarks>
/// Holds only the non-retrievable secrets. The public share reference is delivered separately so that
/// possession of this file alone does not constitute a complete download capability.
/// </remarks>
internal sealed record CredentialDocument(
    [property: JsonPropertyName("shareKey")]
    String ShareKey,
    [property: JsonPropertyName("downloadBearerToken")]
    String? DownloadBearerToken);

/// <summary>
/// Stable status values reported by <see cref="UploadCommandResult.Status"/>.
/// </summary>
internal static class UploadCommandStatus
{
    public const String CredentialDeliveryFailed = "credential-delivery-failed";
    public const String QueueWriteFailed = "queue-write-failed";
    public const String ShareCreationFailed = "share-creation-failed";
    public const String Succeeded = "succeeded";
    public const String UploadFailed = "upload-failed";
}
