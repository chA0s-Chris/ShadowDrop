// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Contracts;
using System.Globalization;

internal static class ShareSummaryMapper
{
    public static ShareListItemContract Map(
        Guid shareId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        ShareCleanupState cleanupState,
        DateTimeOffset? lastCleanupAttemptAtUtc,
        IEnumerable<String>? cleanupFailureCategories,
        Int64 fileCount,
        Int64 ciphertextBytes,
        DateTimeOffset nowUtc) =>
        new(shareId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant(),
            createdAtUtc.ToUniversalTime(),
            expiresAtUtc.ToUniversalTime(),
            revokedAtUtc?.ToUniversalTime(),
            ShareLifecycle.Statuses(expiresAtUtc, revokedAtUtc, cleanupState, nowUtc),
            ShareLifecycle.CleanupState(cleanupState),
            lastCleanupAttemptAtUtc?.ToUniversalTime(),
            ShareLifecycle.FailureCategories(cleanupFailureCategories),
            fileCount,
            ciphertextBytes);
}
