// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

/// <summary>The request-scoped authorization context of an upload credential or the bootstrap administrator.</summary>
public sealed record UploadCredentialAuthorizationContext(
    Guid? CredentialId,
    Int64? MaxEncryptedFileBytes,
    Int64? MaxEncryptedShareBytes)
{
    public static UploadCredentialAuthorizationContext BootstrapAdmin { get; } = new(null, null, null);

    public Boolean IsBootstrapAdmin => CredentialId is null;
}
