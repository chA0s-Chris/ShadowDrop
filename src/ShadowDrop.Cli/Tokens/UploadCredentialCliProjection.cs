// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using System.Text.Json.Serialization;

internal sealed record UploadCredentialCliProjection(
    [property: JsonPropertyName("credentialId")]
    Guid CredentialId,
    [property: JsonPropertyName("name")] String Name,
    [property: JsonPropertyName("createdAtUtc")]
    DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("expiresAtUtc")]
    DateTimeOffset? ExpiresAtUtc,
    [property: JsonPropertyName("revokedAtUtc")]
    DateTimeOffset? RevokedAtUtc,
    [property: JsonPropertyName("lastUsedAtUtc")]
    DateTimeOffset? LastUsedAtUtc,
    [property: JsonPropertyName("capability")]
    String Capability,
    [property: JsonPropertyName("maxEncryptedFileBytes")]
    Int64? MaxEncryptedFileBytes,
    [property: JsonPropertyName("maxEncryptedShareBytes")]
    Int64? MaxEncryptedShareBytes);
