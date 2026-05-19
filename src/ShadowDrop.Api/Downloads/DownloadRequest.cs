// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

public sealed record DownloadRequest(
    DownloadRequestMode Mode,
    String ShareToken,
    Guid FileId,
    String? AuthorizationBearerToken,
    String? HeaderKeyMaterial,
    String? QueryKeyMaterial,
    RequestedByteRange? RequestedRange,
    Boolean HasMalformedRangeHeader = false);

public enum DownloadRequestMode
{
    DirectHttp = 0,
    Cli = 1
}

public sealed record RequestedByteRange(Int64? Start, Int64? EndInclusive);
