// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using System.Security.Cryptography;

/// <summary>
/// Versioned slow hashing for upload-token secrets. Version 1 is PBKDF2-SHA256 with the same parameters the
/// bootstrap admin token uses; the version is persisted per credential so parameters can evolve without
/// rewriting stored records.
/// </summary>
internal static class UploadCredentialSecretHashing
{
    public const Int32 CurrentIterations = 100_000;
    public const Int32 CurrentVersion = 1;
    private const Int32 HashSize = 32;
    private const Int32 SaltSize = 16;

    public static UploadCredentialSecretHash Compute(String secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(secret, salt, CurrentIterations);
        return new(Convert.ToBase64String(hash), Convert.ToBase64String(salt), CurrentIterations, CurrentVersion);
    }

    public static Boolean Matches(String secret, UploadCredentialRecord credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.SecretHashVersion != CurrentVersion)
        {
            return false;
        }

        var salt = Convert.FromBase64String(credential.SecretSaltBase64);
        var expected = Convert.FromBase64String(credential.SecretHashBase64);
        var actual = Derive(secret, salt, credential.SecretHashIterations);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static Byte[] Derive(String secret, Byte[] salt, Int32 iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, HashSize);
}
