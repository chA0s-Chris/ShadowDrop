// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Crypto;

/// <summary>
/// Describes the authenticated metadata bound to an encrypted chunk.
/// </summary>
public sealed record ChunkMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkMetadata"/> record.
    /// </summary>
    /// <param name="version">The crypto format version.</param>
    /// <param name="algorithm">The crypto algorithm identifier.</param>
    /// <param name="fileId">The file identifier.</param>
    /// <param name="chunkSize">The configured plaintext chunk size.</param>
    /// <param name="chunkIndex">The zero-based chunk index.</param>
    /// <param name="plaintextChunkLength">The plaintext length of the chunk.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a numeric argument is outside the supported range.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="version"/> or <paramref name="algorithm"/> is
    /// unsupported.
    /// </exception>
    public ChunkMetadata(
        CryptoVersion version,
        CryptoAlgorithm algorithm,
        Guid fileId,
        Int32 chunkSize,
        Int64 chunkIndex,
        Int32 plaintextChunkLength)
    {
        if (version != CryptoVersion.V1)
        {
            throw new ArgumentException("Only crypto version V1 is supported.", nameof(version));
        }

        if (algorithm != CryptoAlgorithm.Aes256Gcm)
        {
            throw new ArgumentException("Only AES-256-GCM is supported.", nameof(algorithm));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkIndex, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(plaintextChunkLength, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plaintextChunkLength, chunkSize);

        Version = version;
        Algorithm = algorithm;
        FileId = fileId;
        ChunkSize = chunkSize;
        ChunkIndex = chunkIndex;
        PlaintextChunkLength = plaintextChunkLength;
    }

    /// <summary>
    /// Gets the crypto algorithm identifier.
    /// </summary>
    public CryptoAlgorithm Algorithm { get; }

    /// <summary>
    /// Gets the zero-based chunk index.
    /// </summary>
    public Int64 ChunkIndex { get; }

    /// <summary>
    /// Gets the configured plaintext chunk size.
    /// </summary>
    public Int32 ChunkSize { get; }

    /// <summary>
    /// Gets the file identifier.
    /// </summary>
    public Guid FileId { get; }

    /// <summary>
    /// Gets the plaintext length of the chunk.
    /// </summary>
    public Int32 PlaintextChunkLength { get; }

    /// <summary>
    /// Gets the crypto format version.
    /// </summary>
    public CryptoVersion Version { get; }
}
