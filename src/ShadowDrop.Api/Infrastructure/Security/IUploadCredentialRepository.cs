// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public interface IUploadCredentialRepository
{
    /// <summary>
    /// Looks up a credential by its indexed selector digest; used on every authentication and must not scan
    /// all credentials.
    /// </summary>
    Task<UploadCredentialRecord?> FindBySelectorDigestAsync(String selectorDigestBase64, CancellationToken cancellationToken);

    Task<UploadCredentialRecord?> GetAsync(Guid credentialId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="pageSize"/> credentials ordered newest-first, continuing after
    /// <paramref name="cursor"/> when supplied.
    /// </summary>
    Task<UploadCredentialPage> ListNewestFirstAsync(Int32 pageSize, UploadCredentialListCursor? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Monotonically raises the credential's last-used timestamp without touching any other state; concurrent
    /// calls never lower an already newer value.
    /// </summary>
    Task RecordUsageAsync(Guid credentialId, DateTimeOffset lastUsedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Records the first revocation timestamp atomically and idempotently. Returns the resulting record, or
    /// <see langword="null"/> when no credential with the given management id exists.
    /// </summary>
    Task<UploadCredentialRecord?> RevokeAsync(Guid credentialId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the credential; returns <see langword="false"/> when the management id or selector digest is
    /// already in use so the caller can regenerate.
    /// </summary>
    Task<Boolean> TryCreateAsync(UploadCredentialRecord record, CancellationToken cancellationToken);
}
