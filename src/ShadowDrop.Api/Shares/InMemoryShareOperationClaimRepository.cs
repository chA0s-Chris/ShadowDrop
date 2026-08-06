// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

internal sealed class InMemoryShareOperationClaimRepository : IShareOperationClaimRepository
{
    private readonly Dictionary<Guid, ShareOperationClaim> _claims = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastRecoveryInspections = [];
    private readonly Lock _syncRoot = new();

    private static Boolean Matches(
        ShareOperationClaim claim,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds) =>
        claim.Kind == kind
        && claim.ShareId == shareId
        && claim.FileIds.Order().SequenceEqual(fileIds);

    public Task<IReadOnlyList<ShareOperationClaim>> GetSweepClaimsAsync(Int32 limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            IReadOnlyList<ShareOperationClaim> claims =
            [
                .. _claims.Values
                          .Where(claim => claim.Kind == ShareOperationClaimKind.SweepUpload)
                          .OrderBy(claim => _lastRecoveryInspections.ContainsKey(claim.OperationId))
                          .ThenBy(claim => _lastRecoveryInspections.GetValueOrDefault(claim.OperationId))
                          .ThenBy(claim => claim.OperationId)
                          .Take(Math.Max(limit, 0))
            ];
            return Task.FromResult(claims);
        }
    }

    public Task<IReadOnlyList<ShareOperationClaim>> GetUnfinishedShareCreationsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            IReadOnlyList<ShareOperationClaim> claims =
            [
                .. _claims.Values
                          .Where(claim => claim.Kind == ShareOperationClaimKind.CreateShare
                                          && claim.FileIds.Intersect(fileIds).Any())
            ];
            return Task.FromResult(claims);
        }
    }

    public Task<Boolean> TryAbortAcquiredAsync(Guid operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(_claims.TryGetValue(operationId, out var claim)
                                   && claim.Lifecycle == ShareOperationClaimLifecycle.Acquired
                                   && _claims.Remove(operationId));
        }
    }

    public Task<ShareOperationClaim?> TryAcquireAsync(
        Guid operationId,
        ShareOperationClaimKind kind,
        Guid shareId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedFileIds = fileIds.Distinct().Order().ToArray();
        lock (_syncRoot)
        {
            if (_claims.TryGetValue(operationId, out var existing))
            {
                return Task.FromResult(Matches(existing, kind, shareId, normalizedFileIds)
                                           ? existing
                                           : null);
            }

            if (_claims.Values.Any(claim => claim.FileIds.Intersect(normalizedFileIds).Any()))
            {
                return Task.FromResult<ShareOperationClaim?>(null);
            }

            var claim = new ShareOperationClaim(operationId,
                                                kind,
                                                shareId,
                                                normalizedFileIds,
                                                ShareOperationClaimLifecycle.Acquired);
            _claims.Add(operationId, claim);
            return Task.FromResult<ShareOperationClaim?>(claim);
        }
    }

    public Task<Boolean> TryBeginCommitAsync(
        Guid operationId,
        ShareRecord proposedShare,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_claims.TryGetValue(operationId, out var claim)
                || claim.Kind != ShareOperationClaimKind.CreateShare
                || claim.Lifecycle != ShareOperationClaimLifecycle.Acquired
                || claim.ShareId != proposedShare.ShareId)
            {
                return Task.FromResult(false);
            }

            _claims[operationId] = claim with
            {
                Lifecycle = ShareOperationClaimLifecycle.Committing,
                ProposedShare = proposedShare
            };
            return Task.FromResult(true);
        }
    }

    public Task<Boolean> TryRecordSweepClaimInspectionAsync(
        Guid operationId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_claims.TryGetValue(operationId, out var claim) || claim.Kind != ShareOperationClaimKind.SweepUpload)
            {
                return Task.FromResult(false);
            }

            _lastRecoveryInspections[operationId] = inspectedAtUtc;
            return Task.FromResult(true);
        }
    }

    public Task<Boolean> TryReleaseAsync(Guid operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _lastRecoveryInspections.Remove(operationId);
            return Task.FromResult(_claims.Remove(operationId));
        }
    }
}
