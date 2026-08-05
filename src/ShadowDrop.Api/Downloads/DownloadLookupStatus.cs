// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

public enum DownloadLookupStatus
{
    Success = 0,
    InvalidShare = 1,
    Forbidden = 2,
    NotFound = 3,
    InvalidRequest = 4,
    InvalidRange = 5,
    RangeNotSatisfiable = 6
}
