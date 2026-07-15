// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using System.Buffers.Text;
using System.Security.Cryptography;

/// <summary>
/// Generates and parses external upload tokens of the form <c>sdu1.&lt;selector&gt;.&lt;secret&gt;</c>, where the
/// selector carries 128 bits and the secret 256 bits of cryptographic randomness (base64url, unpadded).
/// Only the selector's digest and a slow hash of the secret are ever persisted.
/// </summary>
public static class UploadCredentialToken
{
    public const String Prefix = "sdu1";
    private const Int32 SecretByteCount = 32;
    private const Int32 SecretTextLength = 43;
    private const Int32 SelectorByteCount = 16;
    private const Int32 SelectorTextLength = 22;

    public static UploadCredentialTokenParts Create()
    {
        var selector = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SelectorByteCount));
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretByteCount));
        return new($"{Prefix}.{selector}.{secret}", selector, secret);
    }

    public static Boolean TryParse(String? token, out String selector, out String secret)
    {
        selector = String.Empty;
        secret = String.Empty;
        if (String.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3
            || !String.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || parts[1].Length != SelectorTextLength
            || parts[2].Length != SecretTextLength
            || !Base64Url.IsValid(parts[1])
            || !Base64Url.IsValid(parts[2]))
        {
            return false;
        }

        selector = parts[1];
        secret = parts[2];
        return true;
    }
}
