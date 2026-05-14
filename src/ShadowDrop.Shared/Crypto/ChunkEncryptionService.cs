// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

using System.Buffers.Binary;
using System.Security.Cryptography;

/// <summary>
/// Provides chunk key derivation, authenticated encryption, authenticated decryption, and plaintext range mapping.
/// </summary>
public static class ChunkEncryptionService
{
    private const Int32 AadLength = 50;
    private const Int32 AesGcmTagLength = 16;
    private const Int32 AesKeyLength = 32;
    private const Int32 HkdfInfoLength = 34;
    private const Int32 NonceLength = 12;

    /// <summary>
    /// Decrypts an encrypted chunk with AES-256-GCM.
    /// </summary>
    /// <param name="chunk">The encrypted chunk.</param>
    /// <param name="key">The file-scoped content key.</param>
    /// <param name="metadata">The authenticated metadata for the chunk.</param>
    /// <returns>The decrypted plaintext chunk.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="chunk"/>, <paramref name="key"/>, or
    /// <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="CryptographicException">Thrown when the encrypted payload or metadata is invalid or tampered with.</exception>
    public static Byte[] DecryptChunk(EncryptedChunk chunk, ContentKey key, ChunkMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(metadata);

        if (chunk.Ciphertext.Length < AesGcmTagLength)
        {
            throw new CryptographicException("Encrypted chunks must include a 16-byte authentication tag.");
        }

        var ciphertextBytes = chunk.Ciphertext.AsSpan(0, chunk.Ciphertext.Length - AesGcmTagLength);
        var tagBytes = chunk.Ciphertext.AsSpan(chunk.Ciphertext.Length - AesGcmTagLength);

        if (ciphertextBytes.Length != metadata.PlaintextChunkLength)
        {
            throw new CryptographicException("Encrypted chunk length does not match the supplied metadata.");
        }

        Span<Byte> nonce = stackalloc Byte[NonceLength];
        FillNonce(metadata.ChunkIndex, nonce);

        Span<Byte> aad = stackalloc Byte[AadLength];
        BuildAad(metadata, aad);

        var plaintext = new Byte[ciphertextBytes.Length];

        using var aesGcm = new AesGcm(key.KeyMaterial, AesGcm.TagByteSizes.MaxSize);
        aesGcm.Decrypt(nonce, ciphertextBytes, tagBytes, plaintext, aad);
        return plaintext;
    }

    /// <summary>
    /// Derives a file-scoped AES-256-GCM content key from a share secret and file encryption context.
    /// </summary>
    /// <param name="secret">The share secret.</param>
    /// <param name="context">The file encryption context.</param>
    /// <returns>A new <see cref="ContentKey"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="secret"/> or <paramref name="context"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static ContentKey DeriveContentKey(ShareSecret secret, FileEncryptionContext context)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(context);

        Span<Byte> info = stackalloc Byte[HkdfInfoLength];
        BuildInfoBlob(context, info);

        var keyMaterial = new Byte[AesKeyLength];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret.KeyMaterial, keyMaterial, context.KdfSalt, info);
        return new(keyMaterial);
    }

    /// <summary>
    /// Encrypts a plaintext chunk with AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The plaintext chunk to encrypt.</param>
    /// <param name="key">The file-scoped content key.</param>
    /// <param name="metadata">The authenticated metadata for the chunk.</param>
    /// <returns>The encrypted chunk as ciphertext with the authentication tag appended.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="key"/> or <paramref name="metadata"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="metadata"/> does not match <paramref name="plaintext"/>
    /// .
    /// </exception>
    public static EncryptedChunk EncryptChunk(ReadOnlySpan<Byte> plaintext, ContentKey key, ChunkMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.PlaintextChunkLength != plaintext.Length)
        {
            throw new ArgumentException("The metadata plaintext length must match the plaintext buffer length.", nameof(metadata));
        }

        Span<Byte> nonce = stackalloc Byte[NonceLength];
        FillNonce(metadata.ChunkIndex, nonce);

        Span<Byte> aad = stackalloc Byte[AadLength];
        BuildAad(metadata, aad);

        var ciphertext = new Byte[plaintext.Length];
        var tag = new Byte[AesGcmTagLength];

        using var aesGcm = new AesGcm(key.KeyMaterial, AesGcm.TagByteSizes.MaxSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var output = new Byte[ciphertext.Length + AesGcmTagLength];
        ciphertext.CopyTo(output, 0);
        tag.CopyTo(output, ciphertext.Length);
        return new(output);
    }

    /// <summary>
    /// Maps a plaintext byte range to the chunk indexes and offsets that contain it.
    /// </summary>
    /// <param name="plaintextOffset">The zero-based plaintext byte offset.</param>
    /// <param name="plaintextLength">The plaintext length in bytes.</param>
    /// <param name="chunkSize">The configured plaintext chunk size.</param>
    /// <returns>The chunk range covering the requested plaintext segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an argument is outside the supported range.</exception>
    public static ChunkRange GetChunkRange(Int64 plaintextOffset, Int64 plaintextLength, Int32 chunkSize)
    {
        if (plaintextOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintextOffset), "The plaintext offset must be zero or greater.");
        }

        if (plaintextLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintextLength), "The plaintext length must be greater than zero.");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "The chunk size must be greater than zero.");
        }

        if (plaintextOffset > Int64.MaxValue - plaintextLength + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintextLength), "The requested range exceeds the supported Int64 range.");
        }

        var finalOffset = plaintextOffset + plaintextLength - 1;
        var firstChunkIndex = plaintextOffset / chunkSize;
        var lastChunkIndex = finalOffset / chunkSize;
        var offsetInFirstChunk = (Int32)(plaintextOffset % chunkSize);
        var endOffsetInLastChunk = (Int32)(finalOffset % chunkSize);

        return new(firstChunkIndex, lastChunkIndex, offsetInFirstChunk, endOffsetInLastChunk);
    }

    private static void BuildAad(ChunkMetadata metadata, Span<Byte> destination)
    {
        destination[0] = (Byte)metadata.Version;
        destination[1] = (Byte)metadata.Algorithm;
        metadata.ShareId.ToByteArray().CopyTo(destination[2..18]);
        metadata.FileId.ToByteArray().CopyTo(destination[18..34]);
        BinaryPrimitives.WriteInt32BigEndian(destination[34..38], metadata.ChunkSize);
        BinaryPrimitives.WriteInt64BigEndian(destination[38..46], metadata.ChunkIndex);
        BinaryPrimitives.WriteInt32BigEndian(destination[46..50], metadata.PlaintextChunkLength);
    }

    private static void BuildInfoBlob(FileEncryptionContext context, Span<Byte> destination)
    {
        destination[0] = (Byte)CryptoVersion.V1;
        destination[1] = (Byte)CryptoAlgorithm.Aes256Gcm;
        context.ShareId.ToByteArray().CopyTo(destination[2..18]);
        context.FileId.ToByteArray().CopyTo(destination[18..34]);
    }

    private static void FillNonce(Int64 chunkIndex, Span<Byte> nonce)
    {
        nonce.Clear();
        BinaryPrimitives.WriteInt64BigEndian(nonce[4..], chunkIndex);
    }
}
