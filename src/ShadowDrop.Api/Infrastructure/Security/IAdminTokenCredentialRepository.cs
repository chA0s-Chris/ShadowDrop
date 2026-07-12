// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public interface IAdminTokenCredentialRepository
{
    Task<AdminTokenCredential?> GetAsync(CancellationToken cancellationToken);

    Task<Boolean> TryCreateAsync(AdminTokenCredential credential, CancellationToken cancellationToken);
}
