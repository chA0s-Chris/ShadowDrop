// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

/// <summary>
/// A scoped upload credential. The management <paramref name="CredentialId"/> is deliberately unrelated to the
/// token selector, and no property of this record ever contains plaintext token material.
/// </summary>
public sealed record UploadCredentialRecord(
    Guid CredentialId,
    String Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    Int64? MaxEncryptedFileBytes,
    Int64? MaxEncryptedShareBytes,
    String SelectorDigestBase64,
    String SecretHashBase64,
    String SecretSaltBase64,
    Int32 SecretHashIterations,
    Int32 SecretHashVersion)
{
    /// <summary>The single capability every upload credential carries in this version.</summary>
    public const String FixedCapability = "upload-and-share";
}
