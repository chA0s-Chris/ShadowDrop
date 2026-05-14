// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

/// <summary>
/// Identifies the supported content encryption algorithms.
/// </summary>
public enum CryptoAlgorithm : Byte
{
    /// <summary>
    /// AES-256-GCM.
    /// </summary>
    Aes256Gcm = 0x01
}
