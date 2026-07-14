// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

/// <summary>
/// The created credential together with the plaintext token, which is disclosed exactly once here and is
/// unrecoverable afterward.
/// </summary>
public sealed record UploadCredentialCreationResult(UploadCredentialRecord Credential, String Token);
