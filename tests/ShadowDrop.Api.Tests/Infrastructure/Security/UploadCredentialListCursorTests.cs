// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure.Security;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Infrastructure.Security;

public sealed class UploadCredentialListCursorTests
{
    [Test]
    public void Encode_ShouldRoundTripThroughTryDecode()
    {
        var cursor = new UploadCredentialListCursor(1_752_000_000_123, Guid.NewGuid());

        UploadCredentialListCursor.TryDecode(cursor.Encode(), out var decoded).Should().BeTrue();

        decoded.Should().Be(cursor);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("%%%")]
    [TestCase("bm90LWEtY3Vyc29y")]
    [TestCase("MTIzNDU2")]
    public void TryDecode_ShouldRejectInvalidCursors(String? encoded)
    {
        UploadCredentialListCursor.TryDecode(encoded, out var cursor).Should().BeFalse();

        cursor.Should().BeNull();
    }
}
