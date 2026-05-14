// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Crypto;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Crypto;

public sealed class ShareSecretTests
{
    [Test]
    public void DeriveContentKey_ShouldThrowObjectDisposedException_WhenShareSecretIsDisposed()
    {
        using var secret = ShareSecret.Generate();
        var context = new FileEncryptionContext(Guid.NewGuid(), Guid.NewGuid(), FileEncryptionContext.GenerateKdfSalt());
        secret.Dispose();

        var act = () => ChunkEncryptionService.DeriveContentKey(secret, context);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void FromBytes_ShouldCopyTheFirst32Bytes()
    {
        var bytes = CreateSequentialBytes(40);
        var expected = bytes.Take(32).ToArray();

        using var secret = ShareSecret.FromBytes(bytes);
        bytes[0] = Byte.MaxValue;
        bytes[31] = Byte.MaxValue;

        secret.KeyMaterial.ToArray().Should().Equal(expected);
    }

    [Test]
    public void FromBytes_ShouldThrowArgumentException_WhenInputIsTooShort()
    {
        var bytes = CreateSequentialBytes(31);

        var act = () => ShareSecret.FromBytes(bytes);

        act.Should()
           .Throw<ArgumentException>()
           .WithParameterName("bytes");
    }

    [Test]
    public void KeyMaterial_ShouldThrowObjectDisposedException_WhenSecretIsDisposed()
    {
        using var secret = ShareSecret.Generate();
        secret.Dispose();

        var act = () => secret.KeyMaterial.ToArray();

        act.Should().Throw<ObjectDisposedException>();
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
