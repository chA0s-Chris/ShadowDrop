// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public sealed record UploadResult(
    Guid FileId,
    Int64 PlaintextLength,
    Int64 EncryptedLength,
    Int32 ChunkSize,
    Int64 ChunkCount,
    String EncryptionFormatVersion,
    String AlgorithmId);
