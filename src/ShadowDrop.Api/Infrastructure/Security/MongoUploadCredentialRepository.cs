// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using Chaos.Mongo;
using MongoDB.Driver;
using ShadowDrop.Api.Infrastructure.Mongo;

public sealed class MongoUploadCredentialRepository(IMongoHelper mongo) : IUploadCredentialRepository
{
    private IMongoCollection<MongoUploadCredentialDocument> Collection =>
        mongo.GetCollection<MongoUploadCredentialDocument>();

    private static MongoUploadCredentialDocument Map(UploadCredentialRecord record) => new()
    {
        CredentialId = record.CredentialId,
        Name = record.Name,
        CreatedAtUnixTimeMilliseconds = record.CreatedAtUtc.ToUnixTimeMilliseconds(),
        ExpiresAtUnixTimeMilliseconds = record.ExpiresAtUtc?.ToUnixTimeMilliseconds(),
        RevokedAtUnixTimeMilliseconds = record.RevokedAtUtc?.ToUnixTimeMilliseconds(),
        LastUsedAtUnixTimeMilliseconds = record.LastUsedAtUtc?.ToUnixTimeMilliseconds(),
        MaxEncryptedFileBytes = record.MaxEncryptedFileBytes,
        MaxEncryptedShareBytes = record.MaxEncryptedShareBytes,
        SelectorDigestBase64 = record.SelectorDigestBase64,
        SecretHashBase64 = record.SecretHashBase64,
        SecretSaltBase64 = record.SecretSaltBase64,
        SecretHashIterations = record.SecretHashIterations,
        SecretHashVersion = record.SecretHashVersion
    };

    private static UploadCredentialRecord Map(MongoUploadCredentialDocument document) =>
        new(document.CredentialId,
            document.Name,
            DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.ExpiresAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.RevokedAtUnixTimeMilliseconds),
            ToNullableTimestamp(document.LastUsedAtUnixTimeMilliseconds),
            document.MaxEncryptedFileBytes,
            document.MaxEncryptedShareBytes,
            document.SelectorDigestBase64,
            document.SecretHashBase64,
            document.SecretSaltBase64,
            document.SecretHashIterations,
            document.SecretHashVersion);

    private static DateTimeOffset? ToNullableTimestamp(Int64? unixTimeMilliseconds) =>
        unixTimeMilliseconds is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds.Value);

    public async Task<UploadCredentialRecord?> FindBySelectorDigestAsync(String selectorDigestBase64, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorDigestBase64);
        var document = await Collection.Find(x => x.SelectorDigestBase64 == selectorDigestBase64)
                                       .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<UploadCredentialRecord?> GetAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        var document = await Collection.Find(x => x.CredentialId == credentialId).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<UploadCredentialPage> ListNewestFirstAsync(Int32 pageSize, UploadCredentialListCursor? cursor,
                                                                 CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var filterBuilder = Builders<MongoUploadCredentialDocument>.Filter;
        var filter = cursor is null
            ? filterBuilder.Empty
            : filterBuilder.Or(
                filterBuilder.Lt(x => x.CreatedAtUnixTimeMilliseconds, cursor.CreatedAtUnixTimeMilliseconds),
                filterBuilder.And(
                    filterBuilder.Eq(x => x.CreatedAtUnixTimeMilliseconds, cursor.CreatedAtUnixTimeMilliseconds),
                    filterBuilder.Lt(x => x.CredentialId, cursor.CredentialId)));
        var sort = Builders<MongoUploadCredentialDocument>.Sort
                                                          .Descending(x => x.CreatedAtUnixTimeMilliseconds)
                                                          .Descending(x => x.CredentialId);

        var fetched = await Collection.Find(filter)
                                      .Sort(sort)
                                      .Limit(pageSize + 1)
                                      .ToListAsync(cancellationToken);
        if (fetched.Count <= pageSize)
        {
            return new(fetched.Select(Map).ToList(), null);
        }

        var page = fetched.Take(pageSize).Select(Map).ToList();
        var last = page[^1];
        return new(page, new(last.CreatedAtUtc.ToUnixTimeMilliseconds(), last.CredentialId));
    }

    public async Task RecordUsageAsync(Guid credentialId, DateTimeOffset lastUsedAtUtc, CancellationToken cancellationToken)
    {
        // $max raises the timestamp atomically and monotonically without touching revocation or any other field.
        await Collection.UpdateOneAsync(
            x => x.CredentialId == credentialId,
            Builders<MongoUploadCredentialDocument>.Update.Max(x => x.LastUsedAtUnixTimeMilliseconds,
                                                               lastUsedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds()),
            cancellationToken: cancellationToken);
    }

    public async Task<UploadCredentialRecord?> RevokeAsync(Guid credentialId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
    {
        await Collection.UpdateOneAsync(
            x => x.CredentialId == credentialId && x.RevokedAtUnixTimeMilliseconds == null,
            Builders<MongoUploadCredentialDocument>.Update.Set(x => x.RevokedAtUnixTimeMilliseconds,
                                                               revokedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds()),
            cancellationToken: cancellationToken);
        return await GetAsync(credentialId, cancellationToken);
    }

    public async Task<Boolean> TryCreateAsync(UploadCredentialRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            await Collection.InsertOneAsync(Map(record), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
