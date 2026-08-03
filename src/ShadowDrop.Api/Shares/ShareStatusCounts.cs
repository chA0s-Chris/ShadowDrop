// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed record ShareStatusCounts(
    Int64 Active,
    Int64 Expired,
    Int64 Revoked,
    Int64 CleanupPending,
    Int64 CleanupFailed,
    Int64 CleanupCompleted)
{
    public ShareStatusCounts(Int64 active, Int64 expired, Int64 revoked, Int64 cleanupCompleted, Int64 cleanupFailed)
        : this(active, expired, revoked, 0, cleanupFailed, cleanupCompleted) { }
}
