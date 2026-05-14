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

    /// <summary>
    /// Initializes a new instance of the <see cref="FileEncryptionContext"/> record.
    /// </summary>
    /// <param name="shareId">The share identifier.</param>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="kdfSalt">The 32-byte HKDF salt.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="kdfSalt"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="kdfSalt"/> is not 32 bytes long.</exception>
    public FileEncryptionContext(Guid shareId, Guid fileId, Byte[] kdfSalt)
    {
        ArgumentNullException.ThrowIfNull(kdfSalt);

        if (kdfSalt.Length != KdfSaltLength)
        {
            throw new ArgumentException("The KDF salt must be exactly 32 bytes long.", nameof(kdfSalt));
        }

        ShareId = shareId;
        FileId = fileId;
        KdfSalt = kdfSalt.ToArray();
    }

    /// <summary>
    /// Gets the file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the 32-byte HKDF salt.
    /// </summary>
    public Byte[] KdfSalt { get; }

    /// <summary>
    /// Gets the share identifier.
    /// </summary>
    public Guid ShareId { get; }

    /// <summary>
    /// Generates a new file encryption context with a fresh random salt.
    /// </summary>
    /// <param name="shareId">The share identifier.</param>
    /// <param name="fileId">The file identifier.</param>
    /// <returns>A new <see cref="FileEncryptionContext"/> instance.</returns>
    public static FileEncryptionContext Generate(Guid shareId, Guid fileId) => new(shareId, fileId, RandomNumberGenerator.GetBytes(KdfSaltLength));
}
