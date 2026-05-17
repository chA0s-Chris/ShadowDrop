// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

public enum DownloadLookupStatus
{
    Success = 0,
    InvalidShare = 1,
    ExpiredShare = 2,
    Forbidden = 3,
    NotFound = 4,
    InvalidRequest = 5
}
