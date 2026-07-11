// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;

public sealed class MongoAdminTokenCredentialRepository(IMongoHelper mongo) : IAdminTokenCredentialRepository
{
    private const Int32 CredentialId = 1;

    private IMongoCollection<MongoAdminTokenCredentialDocument> Collection =>
        mongo.GetCollection<MongoAdminTokenCredentialDocument>();

    public async Task<AdminTokenCredential?> GetAsync(CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.Id == CredentialId).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : new(document.TokenHashBase64, document.SaltBase64, document.Iterations);
    }

    public async Task<Boolean> TryCreateAsync(AdminTokenCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        try
        {
            await Collection.InsertOneAsync(new()
            {
                Id = CredentialId,
                TokenHashBase64 = credential.TokenHashBase64,
                SaltBase64 = credential.SaltBase64,
                Iterations = credential.Iterations
            }, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
