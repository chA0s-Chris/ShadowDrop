// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed record ShareCleanupResult(
    Int32 CandidatesScanned,
    Int32 SharesCompleted,
    Int32 BlobsDeleted,
    Int32 BlobsAlreadyMissing,
    Int32 Failures,
    Boolean Skipped = false);
