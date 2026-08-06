// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public enum ShareOperationClaimKind
{
    CreateShare = 0,
    CleanupShare = 1,

    /// <summary>
    /// Reclamation of a single unreferenced completed upload. No share exists, so the claim's share identifier
    /// carries the sweep's operation identifier purely to satisfy the non-null convention.
    /// </summary>
    SweepUpload = 2
}

public enum ShareOperationClaimLifecycle
{
    Acquired = 0,
    Committing = 1
}

public sealed record ShareOperationClaim(
    Guid OperationId,
    ShareOperationClaimKind Kind,
    Guid ShareId,
    IReadOnlyList<Guid> FileIds,
    ShareOperationClaimLifecycle Lifecycle,
    ShareRecord? ProposedShare = null);
