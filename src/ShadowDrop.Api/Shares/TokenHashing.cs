// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using System.Security.Cryptography;
using System.Text;

internal static class TokenHashing
{
    public static String ComputeHashBase64(String token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    public static Boolean MatchesStoredHash(String token, String storedHashBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedHashBase64);

        var presentedHash = Convert.FromBase64String(ComputeHashBase64(token));
        var storedHash = Convert.FromBase64String(storedHashBase64);
        return CryptographicOperations.FixedTimeEquals(presentedHash, storedHash);
    }
}
