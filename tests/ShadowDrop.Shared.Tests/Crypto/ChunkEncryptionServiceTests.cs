// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Crypto;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Crypto;
using System.Security.Cryptography;

public sealed class ChunkEncryptionServiceTests
{
    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenChunkIndexIsTampered()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedMetadata = CreateMetadata(fixture.Context, metadata.ChunkSize, metadata.ChunkIndex + 1, metadata.PlaintextChunkLength);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, key, tamperedMetadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenChunkSizeIsTampered()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedMetadata = CreateMetadata(fixture.Context, metadata.ChunkSize + 1, metadata.ChunkIndex, metadata.PlaintextChunkLength);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, key, tamperedMetadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenCiphertextIsTampered()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedCiphertext = encryptedChunk.Ciphertext.ToArray();
        tamperedCiphertext[0] ^= Byte.MaxValue;
        var tamperedChunk = new EncryptedChunk(tamperedCiphertext);

        var act = () => ChunkEncryptionService.DecryptChunk(tamperedChunk, key, metadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenFileEncryptionContextIsWrong()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var wrongContext = new FileEncryptionContext(Guid.NewGuid(), fixture.Context.KdfSalt);
        using var wrongKey = ChunkEncryptionService.DeriveContentKey(secret, wrongContext);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, wrongKey, metadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenFileIdIsTampered()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedMetadata = new ChunkMetadata(
            metadata.Version,
            metadata.Algorithm,
            Guid.NewGuid(),
            metadata.ChunkSize,
            metadata.ChunkIndex,
            metadata.PlaintextChunkLength,
            metadata.IsFinal);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, key, tamperedMetadata);

        act.Should().Throw<CryptographicException>();
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenIsFinalIsTampered(Boolean isFinal, Boolean tamperedIsFinal)
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length, isFinal);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedMetadata = CreateMetadata(fixture.Context, metadata.ChunkSize, metadata.ChunkIndex, metadata.PlaintextChunkLength, tamperedIsFinal);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, key, tamperedMetadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenPlaintextChunkLengthIsTampered()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var tamperedMetadata = CreateMetadata(fixture.Context, metadata.ChunkSize, metadata.ChunkIndex, metadata.PlaintextChunkLength - 1);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, key, tamperedMetadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DecryptChunk_ShouldThrowCryptographicException_WhenShareSecretIsWrong()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var wrongSecret = ShareSecret.Generate();
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        using var wrongKey = ChunkEncryptionService.DeriveContentKey(wrongSecret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 2, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);

        var act = () => ChunkEncryptionService.DecryptChunk(encryptedChunk, wrongKey, metadata);

        act.Should().Throw<CryptographicException>();
    }

    [Test]
    public void DeriveContentKey_ShouldRemainUsable_AfterShareSecretIsDisposed()
    {
        var fixture = CreateTestFixture();
        var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        secret.Dispose();
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 0, plaintext.Length);

        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);
        var decryptedPlaintext = ChunkEncryptionService.DecryptChunk(encryptedChunk, key, metadata);

        decryptedPlaintext.Should().Equal(plaintext);
    }

    [Test]
    public void EncryptChunk_ShouldThrowArgumentException_WhenMetadataLengthDoesNotMatchPlaintext()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 0, plaintext.Length - 1);

        var act = () => ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);

        act.Should()
           .Throw<ArgumentException>()
           .WithParameterName("metadata");
    }

    [Test]
    public void EncryptChunk_ShouldThrowObjectDisposedException_WhenContentKeyIsDisposed()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 0, plaintext.Length);
        key.Dispose();

        var act = () => ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void EncryptedChunk_Ciphertext_ShouldReturnDefensiveCopy()
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);
        var plaintext = CreatePlaintext(32);
        var metadata = CreateMetadata(fixture.Context, 64, 0, plaintext.Length);
        var encryptedChunk = ChunkEncryptionService.EncryptChunk(plaintext, key, metadata);

        var exposedCiphertext = encryptedChunk.Ciphertext;
        exposedCiphertext[0] ^= Byte.MaxValue;

        var decryptedPlaintext = ChunkEncryptionService.DecryptChunk(encryptedChunk, key, metadata);

        decryptedPlaintext.Should().Equal(plaintext);
    }

    [Test]
    public void EncryptedChunk_ShouldCopyCiphertextPassedToConstructor()
    {
        var ciphertext = Enumerable.Range(0, 32)
                                   .Select(index => (Byte)index)
                                   .ToArray();
        var chunk = new EncryptedChunk(ciphertext);

        ciphertext[0] ^= Byte.MaxValue;

        chunk.Ciphertext[0].Should().Be(0);
    }

    [TestCaseSource(nameof(FullFileRoundTripCases))]
    public void FullFileRoundTrip_ShouldReturnOriginalPlaintext(Int32 chunkSize, Int32 plaintextLength)
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);

        var plaintext = CreatePlaintext(plaintextLength);
        var encryptedChunks = EncryptChunks(plaintext, chunkSize, key, fixture.Context);

        var decryptedPlaintext = DecryptAllChunks(encryptedChunks, key);

        decryptedPlaintext.Should().Equal(plaintext);
    }

    [TestCaseSource(nameof(RangeRoundTripCases))]
    public void RangeRoundTrip_ShouldReturnRequestedPlaintextSlice(
        Int32 chunkSize,
        Int32 plaintextLength,
        Int64 plaintextOffset,
        Int64 rangeLength)
    {
        var fixture = CreateTestFixture();
        using var secret = fixture.Secret;
        using var key = ChunkEncryptionService.DeriveContentKey(secret, fixture.Context);

        var plaintext = CreatePlaintext(plaintextLength);
        var encryptedChunks = EncryptChunks(plaintext, chunkSize, key, fixture.Context);

        var decryptedRange = DecryptRange(
            encryptedChunks,
            plaintextOffset,
            rangeLength,
            chunkSize,
            key);

        var expectedPlaintext = plaintext.AsSpan((Int32)plaintextOffset, (Int32)rangeLength).ToArray();
        decryptedRange.Should().Equal(expectedPlaintext);
    }

    private static ChunkMetadata CreateMetadata(
        FileEncryptionContext context,
        Int32 chunkSize,
        Int64 chunkIndex,
        Int32 plaintextChunkLength,
        Boolean isFinal = false)
    {
        return new(
            CryptoVersion.V1,
            CryptoAlgorithm.Aes256Gcm,
            context.FileId,
            chunkSize,
            chunkIndex,
            plaintextChunkLength,
            isFinal);
    }

    private static Byte[] CreatePlaintext(Int32 length)
    {
        var plaintext = new Byte[length];

        for (var index = 0; index < plaintext.Length; index++)
        {
            plaintext[index] = (Byte)(index % 251);
        }

        return plaintext;
    }

    private static (ShareSecret Secret, FileEncryptionContext Context) CreateTestFixture()
    {
        var secret = ShareSecret.Generate();
        var context = new FileEncryptionContext(Guid.NewGuid(), FileEncryptionContext.GenerateKdfSalt());
        return (secret, context);
    }

    private static Byte[] DecryptAllChunks(
        IReadOnlyList<(ChunkMetadata Metadata, EncryptedChunk Chunk)> encryptedChunks,
        ContentKey key)
    {
        var totalLength = encryptedChunks.Sum(chunk => chunk.Metadata.PlaintextChunkLength);
        var plaintext = new Byte[totalLength];
        var destinationOffset = 0;

        foreach (var (metadata, chunk) in encryptedChunks)
        {
            var decryptedChunk = ChunkEncryptionService.DecryptChunk(chunk, key, metadata);
            decryptedChunk.CopyTo(plaintext, destinationOffset);
            destinationOffset += decryptedChunk.Length;
        }

        return plaintext;
    }

    private static Byte[] DecryptRange(
        IReadOnlyList<(ChunkMetadata Metadata, EncryptedChunk Chunk)> encryptedChunks,
        Int64 plaintextOffset,
        Int64 rangeLength,
        Int32 chunkSize,
        ContentKey key)
    {
        var chunkRange = ChunkEncryptionService.GetChunkRange(plaintextOffset, rangeLength, chunkSize);
        var decryptedRange = new Byte[(Int32)rangeLength];
        var destinationOffset = 0;

        for (var chunkIndex = chunkRange.FirstChunkIndex; chunkIndex <= chunkRange.LastChunkIndex; chunkIndex++)
        {
            var encryptedChunk = encryptedChunks[(Int32)chunkIndex];
            var decryptedChunk = ChunkEncryptionService.DecryptChunk(encryptedChunk.Chunk, key, encryptedChunk.Metadata);
            var sliceOffset = chunkIndex == chunkRange.FirstChunkIndex ? chunkRange.OffsetInFirstChunk : 0;
            var sliceEndExclusive = chunkIndex == chunkRange.LastChunkIndex
                ? chunkRange.EndOffsetInLastChunk + 1
                : decryptedChunk.Length;
            var sliceLength = sliceEndExclusive - sliceOffset;

            decryptedChunk.AsSpan(sliceOffset, sliceLength).CopyTo(decryptedRange.AsSpan(destinationOffset, sliceLength));
            destinationOffset += sliceLength;
        }

        destinationOffset.Should().Be((Int32)rangeLength);
        return decryptedRange;
    }

    private static List<(ChunkMetadata Metadata, EncryptedChunk Chunk)> EncryptChunks(
        Byte[] plaintext,
        Int32 chunkSize,
        ContentKey key,
        FileEncryptionContext context)
    {
        var encryptedChunks = new List<(ChunkMetadata Metadata, EncryptedChunk Chunk)>();
        var chunkCount = (plaintext.Length + chunkSize - 1) / chunkSize;

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunkOffset = chunkIndex * chunkSize;
            var plaintextChunkLength = Math.Min(chunkSize, plaintext.Length - chunkOffset);
            var metadata = CreateMetadata(context, chunkSize, chunkIndex, plaintextChunkLength, chunkIndex == chunkCount - 1);
            var encryptedChunk = ChunkEncryptionService.EncryptChunk(
                plaintext.AsSpan(chunkOffset, plaintextChunkLength),
                key,
                metadata);

            encryptedChunks.Add((metadata, encryptedChunk));
        }

        return encryptedChunks;
    }

    private static IEnumerable<TestCaseData> FullFileRoundTripCases()
    {
        yield return new TestCaseData(64, 64)
            .SetName("FullFileRoundTrip_SingleChunk");
        yield return new TestCaseData(64, 128)
            .SetName("FullFileRoundTrip_TwoChunks");
        yield return new TestCaseData(64, 192)
            .SetName("FullFileRoundTrip_ThreeChunks");
        yield return new TestCaseData(64, 150)
            .SetName("FullFileRoundTrip_PartialLastChunk");
    }

    private static IEnumerable<TestCaseData> RangeRoundTripCases()
    {
        yield return new TestCaseData(64, 256, 0L, 64L)
            .SetName("RangeRoundTrip_AlignedFirstChunkOnly");
        yield return new TestCaseData(64, 256, 192L, 64L)
            .SetName("RangeRoundTrip_AlignedLastChunkOnly");
        yield return new TestCaseData(64, 256, 0L, 256L)
            .SetName("RangeRoundTrip_AlignedAllChunks");
        yield return new TestCaseData(64, 256, 74L, 54L)
            .SetName("RangeRoundTrip_MidChunkStartEndingAtBoundary");
        yield return new TestCaseData(64, 256, 64L, 25L)
            .SetName("RangeRoundTrip_BoundaryStartEndingMidChunk");
        yield return new TestCaseData(64, 256, 70L, 13L)
            .SetName("RangeRoundTrip_SubChunkWithinSingleChunk");
        yield return new TestCaseData(64, 256, 10L, 150L)
            .SetName("RangeRoundTrip_MultiChunkWithNonAlignedBounds");
    }
}
