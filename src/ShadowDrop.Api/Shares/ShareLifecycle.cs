// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Contracts;

internal static class ShareLifecycle
{
    public static String CleanupState(ShareCleanupState state) => state switch
    {
        ShareCleanupState.Failed => ShareListCleanupStates.Failed,
        ShareCleanupState.Completed => ShareListCleanupStates.Completed,
        _ => ShareListCleanupStates.Pending
    };

    public static String[] FailureCategories(IEnumerable<String>? categories)
    {
        var supplied = categories?.ToHashSet(StringComparer.Ordinal) ?? [];
        return [.. ShareCleanupFailureCategories.CanonicalOrder.Where(supplied.Contains)];
    }

    public static Boolean IsActive(DateTimeOffset expiresAtUtc, DateTimeOffset? revokedAtUtc, DateTimeOffset nowUtc) =>
        revokedAtUtc is null && expiresAtUtc > nowUtc;

    public static Boolean IsExpired(DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc) => expiresAtUtc <= nowUtc;

    public static Boolean IsRevoked(DateTimeOffset? revokedAtUtc) => revokedAtUtc is not null;

    public static Boolean Matches(ShareListRecord share, IReadOnlyCollection<String> filters, DateTimeOffset nowUtc)
    {
        if (filters.Count == 0)
        {
            return true;
        }

        return filters.Any(status => status switch
        {
            ShareListStatuses.Active => IsActive(share.ExpiresAtUtc, share.RevokedAtUtc, nowUtc),
            ShareListStatuses.Expired => IsExpired(share.ExpiresAtUtc, nowUtc),
            ShareListStatuses.Revoked => IsRevoked(share.RevokedAtUtc),
            ShareListStatuses.CleanupPending => share.CleanupState == ShareCleanupState.Pending,
            ShareListStatuses.CleanupFailed => share.CleanupState == ShareCleanupState.Failed,
            ShareListStatuses.CleanupCompleted => share.CleanupState == ShareCleanupState.Completed,
            _ => false
        });
    }

    public static String[] Statuses(ShareListRecord share, DateTimeOffset nowUtc)
    {
        var statuses = new List<String>(4);
        if (IsActive(share.ExpiresAtUtc, share.RevokedAtUtc, nowUtc))
        {
            statuses.Add(ShareListStatuses.Active);
        }

        if (IsExpired(share.ExpiresAtUtc, nowUtc))
        {
            statuses.Add(ShareListStatuses.Expired);
        }

        if (IsRevoked(share.RevokedAtUtc))
        {
            statuses.Add(ShareListStatuses.Revoked);
        }

        statuses.Add(share.CleanupState switch
        {
            ShareCleanupState.Failed => ShareListStatuses.CleanupFailed,
            ShareCleanupState.Completed => ShareListStatuses.CleanupCompleted,
            _ => ShareListStatuses.CleanupPending
        });
        return [.. statuses];
    }
}
