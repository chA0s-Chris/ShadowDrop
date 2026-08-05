// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ShadowDrop.Api.Shares;

internal sealed class MongoShareOperationClaimDocument
{
    public List<Guid> FileIds { get; set; } = [];

    [BsonRepresentation(BsonType.String)]
    public ShareOperationClaimKind Kind { get; set; }

    [BsonIgnoreIfNull]
    public Int64? LastRecoveryInspectionAtUnixTimeMilliseconds { get; set; }

    [BsonRepresentation(BsonType.String)]
    public ShareOperationClaimLifecycle Lifecycle { get; set; }

    [BsonId]
    public Guid OperationId { get; set; }

    public String? ProposedShareJson { get; set; }

    public Guid ShareId { get; set; }
}
