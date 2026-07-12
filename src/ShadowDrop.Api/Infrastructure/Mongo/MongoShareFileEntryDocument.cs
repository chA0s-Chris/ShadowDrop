// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

[BsonIgnoreExtraElements]
internal sealed class MongoShareFileEntryDocument
{
    [BsonIgnoreIfNull]
    public String? DisplayName { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid FileId { get; set; }

    public String OriginalFileName { get; set; } = String.Empty;
}
