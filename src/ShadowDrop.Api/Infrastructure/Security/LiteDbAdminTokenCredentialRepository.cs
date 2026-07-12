// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using LiteDB;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LiteDbAdminTokenCredentialRepository : IAdminTokenCredentialRepository, IDisposable
{
    private const Int32 CredentialId = 1;
    private readonly ILiteCollection<LiteDbAdminTokenCredentialDocument> _collection;
    private readonly LiteDatabase _database;
    private readonly String _databasePath;
    private readonly Lock _syncRoot = new();

    public LiteDbAdminTokenCredentialRepository(ShadowDropOptions options)
    {
        _databasePath = options.Metadata.LiteDbPath;
        _database = new(new ConnectionString
        {
            Filename = _databasePath,
            Connection = ConnectionType.Shared
        });
        _collection = _database.GetCollection<LiteDbAdminTokenCredentialDocument>("admin_tokens");
        _collection.EnsureIndex(x => x.Id, true);
        FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
    }

    public Task<AdminTokenCredential?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = _collection.FindById(CredentialId);
        return Task.FromResult(document is null
                                   ? null
                                   : new AdminTokenCredential(document.TokenHashBase64, document.SaltBase64, document.Iterations));
    }

    public Task<Boolean> TryCreateAsync(AdminTokenCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (_collection.Exists(x => x.Id == CredentialId))
            {
                return Task.FromResult(false);
            }

            _collection.Insert(new LiteDbAdminTokenCredentialDocument
            {
                Id = CredentialId,
                TokenHashBase64 = credential.TokenHashBase64,
                SaltBase64 = credential.SaltBase64,
                Iterations = credential.Iterations
            });
            FileSystemAccessPermissions.EnsureOwnerOnlyFile(_databasePath);
            return Task.FromResult(true);
        }
    }

    public void Dispose() => _database.Dispose();
}
