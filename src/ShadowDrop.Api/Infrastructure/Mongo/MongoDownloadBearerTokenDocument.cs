// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson.Serialization.Attributes;

[BsonIgnoreExtraElements]
internal sealed class MongoDownloadBearerTokenDocument
{
    public Int64 ExpiresAtUnixTimeMilliseconds { get; set; }

    public String TokenHashBase64 { get; set; } = String.Empty;
}
