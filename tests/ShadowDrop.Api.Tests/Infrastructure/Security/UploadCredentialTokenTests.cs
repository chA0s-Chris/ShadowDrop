// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure.Security;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Infrastructure.Security;
using System.Buffers.Text;

public sealed class UploadCredentialTokenTests
{
    [Test]
    public void Create_ShouldEmbedRequiredEntropy()
    {
        var parts = UploadCredentialToken.Create();

        Base64Url.DecodeFromChars(parts.Selector).Should().HaveCount(16, "the selector must carry at least 128 bits of randomness");
        Base64Url.DecodeFromChars(parts.Secret).Should().HaveCount(32, "the secret must carry at least 256 bits of randomness");
    }

    [Test]
    public void Create_ShouldProduceParseableToken()
    {
        var parts = UploadCredentialToken.Create();

        parts.Token.Should().Be($"{UploadCredentialToken.Prefix}.{parts.Selector}.{parts.Secret}");
        UploadCredentialToken.TryParse(parts.Token, out var selector, out var secret).Should().BeTrue();
        selector.Should().Be(parts.Selector);
        secret.Should().Be(parts.Secret);
    }

    [Test]
    public void Create_ShouldProduceUniqueTokens()
    {
        var tokens = Enumerable.Range(0, 32).Select(_ => UploadCredentialToken.Create()).ToList();

        tokens.Select(x => x.Selector).Distinct().Should().HaveCount(tokens.Count);
        tokens.Select(x => x.Secret).Distinct().Should().HaveCount(tokens.Count);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("sdu1")]
    [TestCase("sdu1.only-one-part")]
    [TestCase("sdu2.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("sdu1.short.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("sdu1.AAAAAAAAAAAAAAAAAAAAAA.short")]
    [TestCase("sdu1.AAAAAAAAAAAAAAAAAAAA!A.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("sdu1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [TestCase("sdu1.AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.extra")]
    public void TryParse_ShouldRejectMalformedTokens(String? token)
    {
        UploadCredentialToken.TryParse(token, out _, out _).Should().BeFalse();
    }
}
