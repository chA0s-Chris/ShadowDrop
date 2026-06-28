// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public interface IShareMetadataRepository
{
    /// <summary>
    /// Persists a new share record, enforcing that uploaded files are single-use across shares.
    /// </summary>
    /// <exception cref="CreateShareValidationException">
    /// A file referenced by <paramref name="record"/> is already referenced by an existing share,
    /// regardless of that share's cleanup state.
    /// </exception>
    Task CreateAsync(ShareRecord record, CancellationToken cancellationToken);

    Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken);

    Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken);

    Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken);
}
