// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Contracts;

public sealed record ShareListQuery(
    DateTimeOffset NowUtc,
    String[] Statuses,
    Int32 PageSize,
    ShareListCursor? Cursor);

public sealed record ShareListRecord(
    Guid ShareId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    ShareCleanupState CleanupState,
    DateTimeOffset? LastCleanupAttemptAtUtc,
    IReadOnlyList<String> CleanupFailureCategories,
    IReadOnlyList<Guid> FileIds);

public sealed record ShareListRepositoryPage(
    IReadOnlyList<ShareListRecord> Shares,
    ShareListCursor? NextCursor);
