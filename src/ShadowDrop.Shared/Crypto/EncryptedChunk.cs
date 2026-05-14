// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

/// <summary>
/// Represents an encrypted chunk as ciphertext with the authentication tag appended.
/// </summary>
public sealed record EncryptedChunk
{
    private const Int32 TagLength = 16;
    private readonly Byte[] _ciphertext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedChunk"/> record.
    /// </summary>
    /// <param name="ciphertext">The ciphertext bytes with the 16-byte GCM tag appended.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ciphertext"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ciphertext"/> is shorter than the authentication tag.</exception>
    public EncryptedChunk(Byte[] ciphertext)
        : this(ciphertext, false) { }

    internal EncryptedChunk(Byte[] ciphertext, Boolean takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length < TagLength)
        {
            throw new ArgumentException("Encrypted chunks must include a 16-byte authentication tag.", nameof(ciphertext));
        }

        _ciphertext = takeOwnership ? ciphertext : ciphertext.ToArray();
    }

    /// <summary>
    /// Gets the ciphertext bytes with the 16-byte GCM tag appended.
    /// </summary>
    public Byte[] Ciphertext => _ciphertext.ToArray();

    internal ReadOnlySpan<Byte> CiphertextBytes => _ciphertext;
}
