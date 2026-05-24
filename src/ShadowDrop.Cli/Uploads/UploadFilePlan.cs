// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Crypto;

internal sealed record UploadFilePlan(
    FileInfo File,
    Guid FileId,
    FileEncryptionContext EncryptionContext,
    UploadMetadataPayload Metadata,
    Int32 ChunkSize);
