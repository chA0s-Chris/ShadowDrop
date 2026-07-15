// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

public sealed record CreateUploadCredentialRequest(
    String? Name,
    DateTimeOffset? ExpiresAtUtc,
    Int64? MaxEncryptedFileBytes,
    Int64? MaxEncryptedShareBytes);
