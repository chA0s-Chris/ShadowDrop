// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

using System.Security.Cryptography;

/// <summary>
/// Wraps the 32-byte content encryption key derived for a file.
/// </summary>
public sealed class ContentKey : IDisposable
{
    private const Int32 KeyLength = 32;

    private readonly Byte[] _keyMaterial;
    private Boolean _disposed;

    internal ContentKey(Byte[] keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);

        if (keyMaterial.Length != KeyLength)
        {
            throw new ArgumentException("Content keys must be exactly 32 bytes long.", nameof(keyMaterial));
        }

        _keyMaterial = keyMaterial.ToArray();
    }

    internal ReadOnlySpan<Byte> KeyMaterial
    {
        get
        {
            ThrowIfDisposed();
            return _keyMaterial;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ContentKey));

    /// <summary>
    /// Zeros the wrapped key material.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_keyMaterial);
        _disposed = true;
    }
}
