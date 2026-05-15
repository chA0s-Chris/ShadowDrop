// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Defines stable transport names for direct-download key material.
/// </summary>
public static class DownloadKeyConstants
{
    /// <summary>
    /// The HTTP header name used by CLI and script clients to provide key material.
    /// </summary>
    public const String HeaderName = "ShadowDrop-Key";

    /// <summary>
    /// The browser-compatible query parameter name used to provide key material.
    /// </summary>
    public const String QueryParameterName = "sd-key";
}
