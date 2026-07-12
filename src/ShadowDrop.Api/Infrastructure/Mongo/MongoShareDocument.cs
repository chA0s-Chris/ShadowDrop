// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

[BsonIgnoreExtraElements]
internal sealed class MongoShareDocument
{
    public String CleanupState { get; set; } = String.Empty;

    public Int64 CreatedAtUnixTimeMilliseconds { get; set; }

    [BsonIgnoreIfDefault]
    public Boolean DirectHttpEnabled { get; set; }

    [BsonIgnoreIfNull]
    public MongoDownloadBearerTokenDocument? DownloadBearerToken { get; set; }

    public Int64 ExpiresAtUnixTimeMilliseconds { get; set; }

    public List<MongoShareFileEntryDocument> Files { get; set; } = [];

    [BsonIgnoreIfNull]
    public Int64? RevokedAtUnixTimeMilliseconds { get; set; }

    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid ShareId { get; set; }

    public String ShareTokenHashBase64 { get; set; } = String.Empty;
}
