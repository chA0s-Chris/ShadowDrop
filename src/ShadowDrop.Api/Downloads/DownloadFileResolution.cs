// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using ShadowDrop.Contracts;

public sealed record DownloadFileResolution(
    DownloadMode Mode,
    Guid ShareId,
    Guid FileId,
    String FileName,
    String FileContentType,
    String ResponseContentType,
    Int64 ResponseContentLength,
    Stream ContentStream,
    Int64 TotalPlaintextLength,
    RequestedPlaintextRangeContract? RequestedRange,
    CliDownloadMetadataContract? CliMetadata);
