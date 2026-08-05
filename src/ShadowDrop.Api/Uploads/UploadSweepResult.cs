// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

/// <summary>
/// Outcome of one unreferenced-upload sweep. <paramref name="UploadsDeleted"/> and
/// <paramref name="BlobsAlreadyMissing"/> are disjoint: an upload whose ciphertext was already gone is counted
/// only as already-missing, so the first run after an upgrade cannot report freed storage it never freed.
/// </summary>
public sealed record UploadSweepResult(
    Int32 CandidatesInspected,
    Int32 UploadsDeleted,
    Int32 BlobsAlreadyMissing,
    Int32 Failures);
