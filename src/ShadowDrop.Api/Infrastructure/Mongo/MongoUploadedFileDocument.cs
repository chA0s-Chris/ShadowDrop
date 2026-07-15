// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Mongo;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

[BsonIgnoreExtraElements]
internal sealed class MongoUploadedFileDocument
{
    public String AlgorithmId { get; set; } = String.Empty;

    public String BlobKey { get; set; } = String.Empty;

    [BsonIgnoreIfDefault]
    public Int64 ChunkCount { get; set; }

    [BsonIgnoreIfDefault]
    public Int32 ChunkSize { get; set; }

    [BsonIgnoreIfNull]
    public String? ContentType { get; set; }

    [BsonIgnoreIfDefault]
    public Int64 EncryptedLength { get; set; }

    public String EncryptionFormatVersion { get; set; } = String.Empty;

    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid FileId { get; set; }

    public Boolean IsClaimed { get; set; }

    public Boolean IsReserved { get; set; }

    public String KdfSaltBase64 { get; set; } = String.Empty;

    public String OriginalFileName { get; set; } = String.Empty;

    [BsonIgnoreIfNull]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? OwnerCredentialId { get; set; }

    [BsonIgnoreIfDefault]
    public Int64 PlaintextLength { get; set; }

    [BsonIgnoreIfNull]
    public String? PlaintextSha256 { get; set; }

    [BsonIgnoreIfNull]
    public Int64? ReservedAtUnixTimeMilliseconds { get; set; }
}
