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
    [property: JsonPropertyName("skipped")]
    Boolean Skipped);
