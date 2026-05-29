// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using System.Text.Json.Serialization;

internal sealed record CreateShareCliRequest(
    [property: JsonPropertyName("expiresAtUtc")]
    DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("files")] IReadOnlyList<CreateShareCliFileRequest> Files,
    [property: JsonPropertyName("directHttpEnabled")]
    Boolean DirectHttpEnabled,
    [property: JsonPropertyName("generateDownloadBearerToken")]
    Boolean GenerateDownloadBearerToken,
    [property: JsonPropertyName("downloadBearerTokenExpiresAtUtc")]
    DateTimeOffset? DownloadBearerTokenExpiresAtUtc);

internal sealed record CreateShareCliFileRequest(
    [property: JsonPropertyName("fileId")] Guid FileId,
    [property: JsonPropertyName("displayName")]
    String? DisplayName = null);

internal sealed record CreateShareCliResult(
    [property: JsonPropertyName("shareId")]
    Guid ShareId,
    [property: JsonPropertyName("shareToken")]
    String ShareToken,
    [property: JsonPropertyName("downloadBearerToken")]
    String? DownloadBearerToken);
