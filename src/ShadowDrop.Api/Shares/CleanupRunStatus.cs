// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class CleanupRunStatus
{
    public const String Failure = "failure";
    public const String NotRun = "not-run";
    public const String PartialFailure = "partial-failure";
    public const String Skipped = "skipped";
    public const String Success = "success";

    private CleanupRunStatusSnapshot _snapshot = new(null, NotRun);

    public CleanupRunStatusSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Record(DateTimeOffset completedAtUtc, String outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        Volatile.Write(ref _snapshot, new(completedAtUtc, outcome));
    }
}

public sealed record CleanupRunStatusSnapshot(DateTimeOffset? LastRunAtUtc, String LastOutcome);
