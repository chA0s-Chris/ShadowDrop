// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

using System.Security.Cryptography;

/// <summary>
/// Holds the file-specific inputs required to derive a content key.
/// </summary>
public sealed record FileEncryptionContext
{
    private const Int32 KdfSaltLength = 32;
    private readonly Byte[] _kdfSalt;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileEncryptionContext"/> record.
    /// </summary>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="kdfSalt">The 32-byte file-level HKDF salt.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="kdfSalt"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="kdfSalt"/> is not 32 bytes long.</exception>
    public FileEncryptionContext(Guid fileId, Byte[] kdfSalt)
    {
        ArgumentNullException.ThrowIfNull(kdfSalt);

        if (kdfSalt.Length != KdfSaltLength)
        {
            throw new ArgumentException("The KDF salt must be exactly 32 bytes long.", nameof(kdfSalt));
        }

        FileId = fileId;
        _kdfSalt = kdfSalt.ToArray();
    }

    /// <summary>
    /// Gets the file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the 32-byte file-level HKDF salt.
    /// </summary>
    public Byte[] KdfSalt => _kdfSalt.ToArray();

    internal ReadOnlySpan<Byte> KdfSaltBytes => _kdfSalt;

    /// <summary>
    /// Generates a new random 32-byte file-level HKDF salt.
    /// </summary>
    /// <returns>A new file-level HKDF salt.</returns>
    public static Byte[] GenerateKdfSalt() => RandomNumberGenerator.GetBytes(KdfSaltLength);
}
