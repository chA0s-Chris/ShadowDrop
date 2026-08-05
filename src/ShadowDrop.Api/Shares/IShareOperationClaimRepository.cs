// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public interface IShareOperationClaimRepository
{
    /// <summary>
    /// Returns the unfinished share-creation claims that hold at least one of <paramref name="fileIds"/>,
    /// so reconciliation costs scale with the conflict set rather than the claim collection.
    /// </summary>
    Task<IReadOnlyList<ShareOperationClaim>> GetUnfinishedShareCreationsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken);

    Task<Boolean> TryAbortAcquiredAsync(Guid operationId, CancellationToken cancellationToken);

    Task<ShareOperationClaim?> TryAcquireAsync(
        Guid operationId,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken);

    Task<Boolean> TryBeginCommitAsync(
        Guid operationId,
        ShareRecord proposedShare,
        CancellationToken cancellationToken);

    Task<Boolean> TryReleaseAsync(Guid operationId, CancellationToken cancellationToken);
}
