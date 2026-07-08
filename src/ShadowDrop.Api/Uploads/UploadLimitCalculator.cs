// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

internal static class UploadLimitCalculator
{
    // The advertised file payload limit leaves room for the multipart metadata section,
    // section headers, boundaries, and minor client implementation variance.
    public const Int64 MultipartEnvelopeAllowanceBytes = 128L * 1024;

    public static Int64 ResolveMaxFilePayloadBytes(Int64 maxUploadBodyBytes) =>
        Math.Max(0, maxUploadBodyBytes - MultipartEnvelopeAllowanceBytes);
}
