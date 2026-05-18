// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Defines stable HTTP header names used by public download responses.
/// </summary>
public static class DownloadHeaderConstants
{
    /// <summary>
    /// The header describing the original file content type for CLI decrypt responses.
    /// </summary>
    public const String FileContentTypeHeaderName = "X-ShadowDrop-File-Content-Type";

    /// <summary>
    /// The header describing the intended output file name for download responses.
    /// </summary>
    public const String FileNameHeaderName = "X-ShadowDrop-File-Name";

    /// <summary>
    /// The header describing whether the server returned direct plaintext or CLI decrypt metadata.
    /// </summary>
    public const String ModeHeaderName = "X-ShadowDrop-Download-Mode";
}
