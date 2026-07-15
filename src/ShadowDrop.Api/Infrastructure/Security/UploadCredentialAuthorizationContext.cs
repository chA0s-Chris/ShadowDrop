// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

/// <summary>The request-scoped authorization context of a successfully authenticated upload credential.</summary>
public sealed record UploadCredentialAuthorizationContext(
    Guid CredentialId,
    Int64? MaxEncryptedFileBytes,
    Int64? MaxEncryptedShareBytes);
