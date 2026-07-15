// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using System.Security.Cryptography;

public sealed class AdminTokenService(IAdminTokenCredentialRepository repository, ILogger<AdminTokenService> logger)
{
    private const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
    private const Int32 SaltSize = 16;
    private const Int32 TokenHashIterations = 100_000;
    private const Int32 TokenHashSize = 32;
    private AdminTokenCredential? _credential;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var credential = await repository.GetAsync(cancellationToken);
        if (credential is null)
        {
            var bootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable)?.Trim();
            if (String.IsNullOrWhiteSpace(bootstrapToken))
            {
                throw new InvalidOperationException(
                    $"The environment variable '{BootstrapTokenEnvironmentVariable}' is required on first startup.");
            }

            if (UploadCredentialToken.IsInReservedNamespace(bootstrapToken))
            {
                throw new InvalidOperationException(
                    $"The environment variable '{BootstrapTokenEnvironmentVariable}' must not use the reserved " +
                    $"'{UploadCredentialToken.Prefix}.' upload-credential prefix.");
            }

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var createdCredential = new AdminTokenCredential(
                Convert.ToBase64String(HashToken(bootstrapToken, salt, TokenHashIterations)),
                Convert.ToBase64String(salt),
                TokenHashIterations);
            if (await repository.TryCreateAsync(createdCredential, cancellationToken))
            {
                credential = createdCredential;
                logger.LogInformation("Bootstrap admin token was initialized");
            }
            else
            {
                credential = await repository.GetAsync(cancellationToken)
                             ?? throw new InvalidOperationException("The admin token credential could not be initialized.");
            }
        }

        _credential = credential;
    }

    public Boolean IsValidToken(String token)
    {
        if (String.IsNullOrWhiteSpace(token) || _credential is null)
        {
            return false;
        }

        var salt = Convert.FromBase64String(_credential.SaltBase64);
        var expected = Convert.FromBase64String(_credential.TokenHashBase64);
        var actual = HashToken(token, salt, _credential.Iterations);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static Byte[] HashToken(String token, Byte[] salt, Int32 iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(token, salt, iterations, HashAlgorithmName.SHA256, TokenHashSize);
}
