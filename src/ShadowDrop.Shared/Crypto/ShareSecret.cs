// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

using System.Security.Cryptography;

/// <summary>
/// Wraps the 32-byte share secret used to derive per-file content keys.
/// </summary>
public sealed class ShareSecret : IDisposable
{
    private const Int32 KeyLength = 32;

    private readonly Byte[] _keyMaterial;
    private Boolean _disposed;

    private ShareSecret(Byte[] keyMaterial)
    {
        _keyMaterial = keyMaterial;
    }

    /// <summary>
    /// Gets the key material for this share secret.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the secret has already been disposed.</exception>
    public ReadOnlySpan<Byte> KeyMaterial
    {
        get
        {
            ThrowIfDisposed();
            return _keyMaterial;
        }
    }

    /// <summary>
    /// Creates a share secret from a 32-byte buffer.
    /// </summary>
    /// <param name="bytes">The buffer containing the share secret material.</param>
    /// <returns>A new <see cref="ShareSecret"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="bytes"/> is not exactly 32 bytes long.</exception>
    public static ShareSecret FromBytes(ReadOnlySpan<Byte> bytes)
    {
        if (bytes.Length != KeyLength)
        {
            throw new ArgumentException("Share secrets must contain exactly 32 bytes of key material.", nameof(bytes));
        }

        var keyMaterial = bytes.ToArray();
        return new(keyMaterial);
    }

    /// <summary>
    /// Generates a new random 32-byte share secret.
    /// </summary>
    /// <returns>A new <see cref="ShareSecret"/> instance.</returns>
    public static ShareSecret Generate() => new(RandomNumberGenerator.GetBytes(KeyLength));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ShareSecret));

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
