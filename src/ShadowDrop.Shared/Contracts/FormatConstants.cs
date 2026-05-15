// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Defines stable shared format names and version strings.
/// </summary>
public static class FormatConstants
{
    /// <summary>
    /// The stable algorithm identifier for AES-256-GCM content encryption.
    /// </summary>
    public const String Aes256GcmAlgorithmId = "aes-256-gcm";

    /// <summary>
    /// The initial shared encryption file metadata format version.
    /// </summary>
    public const String EncryptionFormatVersion = "1.0";

    /// <summary>
    /// The current queue file format version.
    /// </summary>
    public const String QueueVersion = "1.0";
    /// <summary>
    /// The current ShadowDrop queue file marker version.
    /// </summary>
    public const String ShadowDropVersion = "1.0";
}
