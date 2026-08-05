// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

/// <summary>
/// A completed upload the unreferenced-upload sweep may inspect. <paramref name="CompletedAtUtc"/> is null
/// for a legacy record written before completion timestamps existed; such a record is stamped on inspection
/// and only becomes eligible a full grace period after that stamp.
/// </summary>
public sealed record UploadSweepCandidate(Guid FileId, String BlobKey, DateTimeOffset? CompletedAtUtc);
