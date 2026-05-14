// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Crypto;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Crypto;

public sealed class CryptoValidationTests
{
    [Test]
    public void ChunkMetadata_ShouldThrowArgumentException_WhenAlgorithmIsUnsupported()
    {
        var act = () => new ChunkMetadata(
            CryptoVersion.V1,
            (CryptoAlgorithm)99,
            Guid.NewGuid(),
            Guid.NewGuid(),
            64,
            0,
            32);

        act.Should()
           .Throw<ArgumentException>()
           .WithParameterName("algorithm");
    }

    [Test]
    public void ChunkMetadata_ShouldThrowArgumentException_WhenVersionIsUnsupported()
    {
        var act = () => new ChunkMetadata(
            (CryptoVersion)99,
            CryptoAlgorithm.Aes256Gcm,
            Guid.NewGuid(),
            Guid.NewGuid(),
            64,
            0,
            32);

        act.Should()
           .Throw<ArgumentException>()
           .WithParameterName("version");
    }

    [Test]
    public void ChunkMetadata_ShouldThrowArgumentOutOfRangeException_WhenPlaintextChunkLengthExceedsChunkSize()
    {
        var act = () => new ChunkMetadata(
            CryptoVersion.V1,
            CryptoAlgorithm.Aes256Gcm,
            Guid.NewGuid(),
            Guid.NewGuid(),
            64,
            0,
            65);

        act.Should()
           .Throw<ArgumentOutOfRangeException>()
           .WithParameterName("plaintextChunkLength");
    }

    [TestCase(31)]
    [TestCase(33)]
    public void FileEncryptionContext_ShouldThrowArgumentException_WhenKdfSaltLengthIsInvalid(Int32 saltLength)
    {
        var salt = new Byte[saltLength];

        var act = () => new FileEncryptionContext(Guid.NewGuid(), Guid.NewGuid(), salt);

        act.Should()
           .Throw<ArgumentException>()
           .WithParameterName("kdfSalt");
    }
}
