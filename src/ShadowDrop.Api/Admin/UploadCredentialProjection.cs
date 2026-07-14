// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

using ShadowDrop.Api.Infrastructure.Security;

/// <summary>
/// The allow-listed administrative view of an upload credential. Selector digests, secret hashes, salts, and
/// hash parameters are deliberately absent.
/// </summary>
public sealed record UploadCredentialProjection(
    Guid CredentialId,
    String Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    String Capability,
    Int64? MaxEncryptedFileBytes,
    Int64? MaxEncryptedShareBytes)
{
    public static UploadCredentialProjection FromRecord(UploadCredentialRecord record) =>
        new(record.CredentialId,
            record.Name,
            record.CreatedAtUtc,
            record.ExpiresAtUtc,
            record.RevokedAtUtc,
            record.LastUsedAtUtc,
            UploadCredentialRecord.FixedCapability,
            record.MaxEncryptedFileBytes,
            record.MaxEncryptedShareBytes);
}
