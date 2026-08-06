// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

/// <summary>
/// Outcome of one cleanup run. <paramref name="Failures"/> is the run total and therefore includes
/// <paramref name="SweepFailures"/>, so a run in which only the unreferenced-upload sweep failed is still
/// reported as a partial failure. The sweep counters are zero on a result produced by the share phase alone.
/// </summary>
public sealed record ShareCleanupResult(
    Int32 CandidatesScanned,
    Int32 SharesCompleted,
    Int32 BlobsDeleted,
    Int32 BlobsAlreadyMissing,
    Int32 Failures,
    Int32 SweepCandidatesInspected = 0,
    Int32 SweepUploadsDeleted = 0,
    Int32 SweepBlobsAlreadyMissing = 0,
    Int32 SweepFailures = 0,
    Boolean Skipped = false);
