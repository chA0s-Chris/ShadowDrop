// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

[BsonIgnoreExtraElements]
internal sealed class MongoUploadCredentialDocument
{
    public Int64 CreatedAtUnixTimeMilliseconds { get; set; }

    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid CredentialId { get; set; }

    [BsonIgnoreIfNull]
    public Int64? ExpiresAtUnixTimeMilliseconds { get; set; }

    [BsonIgnoreIfNull]
    public Int64? LastUsedAtUnixTimeMilliseconds { get; set; }

    [BsonIgnoreIfNull]
    public Int64? MaxEncryptedFileBytes { get; set; }

    [BsonIgnoreIfNull]
    public Int64? MaxEncryptedShareBytes { get; set; }

    public String Name { get; set; } = String.Empty;

    [BsonIgnoreIfNull]
    public Int64? RevokedAtUnixTimeMilliseconds { get; set; }

    public String SecretHashBase64 { get; set; } = String.Empty;

    public Int32 SecretHashIterations { get; set; }

    public Int32 SecretHashVersion { get; set; }

    public String SecretSaltBase64 { get; set; } = String.Empty;

    public String SelectorDigestBase64 { get; set; } = String.Empty;
}
