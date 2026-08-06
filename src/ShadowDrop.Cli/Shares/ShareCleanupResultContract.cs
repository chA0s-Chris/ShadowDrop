// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

using System.Text.Json.Serialization;

internal sealed record ShareCleanupResultContract(
    [property: JsonPropertyName("candidatesScanned")]
    Int32 CandidatesScanned,
    [property: JsonPropertyName("sharesCompleted")]
    Int32 SharesCompleted,
    [property: JsonPropertyName("blobsDeleted")]
    Int32 BlobsDeleted,
    [property: JsonPropertyName("blobsAlreadyMissing")]
    Int32 BlobsAlreadyMissing,
    [property: JsonPropertyName("failures")]
    Int32 Failures,
    [property: JsonPropertyName("sweepCandidatesInspected")]
    Int32 SweepCandidatesInspected,
    [property: JsonPropertyName("sweepUploadsDeleted")]
    Int32 SweepUploadsDeleted,
    [property: JsonPropertyName("sweepBlobsAlreadyMissing")]
    Int32 SweepBlobsAlreadyMissing,
    [property: JsonPropertyName("sweepFailures")]
    Int32 SweepFailures,
    [property: JsonPropertyName("skipped")]
    Boolean Skipped);
