// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;
using System.Security.Cryptography;

public sealed class AdminTokenService : IDisposable
{
    private const Int32 BootstrapCredentialId = 1;
    private const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
    private const Int32 SaltSize = 16;
    private const Int32 TokenHashIterations = 100_000;
    private const Int32 TokenHashSize = 32;
    private readonly ILiteCollection<AdminTokenCredential> _credentials;
    private readonly LiteDatabase _database;

    public AdminTokenService(ShadowDropOptions options, ILogger<AdminTokenService> logger)
    {
        var databaseDirectory = Path.GetDirectoryName(options.Metadata.LiteDbPath)
                                ?? throw new InvalidOperationException("The metadata database path must include a directory.");
        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(databaseDirectory);
        _database = new(new ConnectionString
        {
            Filename = options.Metadata.LiteDbPath,
            Connection = ConnectionType.Shared
        });

        try
        {
            _credentials = _database.GetCollection<AdminTokenCredential>("admin_tokens");
            _credentials.EnsureIndex(credential => credential.Id, true);
            EnsureBootstrapCredential(logger);
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(options.Metadata.LiteDbPath);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    public Boolean IsValidToken(String token)
    {
        if (String.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var credential = _credentials.FindById(BootstrapCredentialId);
        if (credential is null)
        {
            return false;
        }

        var saltBytes = Convert.FromBase64String(credential.SaltBase64);
        var expectedHashBytes = Convert.FromBase64String(credential.TokenHashBase64);
        var actualHashBytes = HashToken(token, saltBytes, credential.Iterations);
        return CryptographicOperations.FixedTimeEquals(expectedHashBytes, actualHashBytes);
    }

    private static Byte[] HashToken(String token, Byte[] saltBytes, Int32 iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            token,
            saltBytes,
            iterations,
            HashAlgorithmName.SHA256,
            TokenHashSize);

    private void EnsureBootstrapCredential(ILogger<AdminTokenService> logger)
    {
        if (_credentials.FindById(BootstrapCredentialId) is not null)
        {
            return;
        }

        var bootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable)?.Trim();
        if (String.IsNullOrWhiteSpace(bootstrapToken))
        {
            throw new InvalidOperationException(
                $"The environment variable '{BootstrapTokenEnvironmentVariable}' is required on first startup.");
        }

        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = HashToken(bootstrapToken, saltBytes, TokenHashIterations);

        _credentials.Upsert(new AdminTokenCredential
        {
            Id = BootstrapCredentialId,
            SaltBase64 = Convert.ToBase64String(saltBytes),
            TokenHashBase64 = Convert.ToBase64String(hashBytes),
            Iterations = TokenHashIterations
        });
        logger.LogInformation("Bootstrap admin token was initialized.");
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    private sealed class AdminTokenCredential
    {
        public Int32 Id { get; set; }

        public Int32 Iterations { get; set; }

        public String SaltBase64 { get; set; } = String.Empty;

        public String TokenHashBase64 { get; set; } = String.Empty;
    }
}
