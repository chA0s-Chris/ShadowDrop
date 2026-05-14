// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Crypto;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Crypto;

public sealed class FileEncryptionContextTests
{
    [Test]
    public void Constructor_ShouldCopyInputSalt()
    {
        var salt = CreateSequentialBytes(32);
        var expectedSalt = salt.ToArray();

        var context = new FileEncryptionContext(Guid.NewGuid(), Guid.NewGuid(), salt);
        salt[0] = Byte.MaxValue;
        salt[31] = Byte.MaxValue;

        context.KdfSalt.Should().Equal(expectedSalt);
    }

    [Test]
    public void GenerateKdfSalt_ShouldReturnRandom32ByteSalt()
    {
        var firstSalt = FileEncryptionContext.GenerateKdfSalt();
        var secondSalt = FileEncryptionContext.GenerateKdfSalt();

        firstSalt.Should().HaveCount(32);
        secondSalt.Should().HaveCount(32);
        firstSalt.Should().NotBeSameAs(secondSalt);
        firstSalt.Should().NotEqual(secondSalt);
    }

    [Test]
    public void KdfSalt_ShouldReturnDefensiveCopy()
    {
        var context = new FileEncryptionContext(Guid.NewGuid(), Guid.NewGuid(), CreateSequentialBytes(32));

        var firstRead = context.KdfSalt;
        firstRead[0] = Byte.MaxValue;

        context.KdfSalt[0].Should().NotBe(Byte.MaxValue);
    }

    private static Byte[] CreateSequentialBytes(Int32 length)
    {
        var bytes = new Byte[length];

        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (Byte)index;
        }

        return bytes;
    }
}
