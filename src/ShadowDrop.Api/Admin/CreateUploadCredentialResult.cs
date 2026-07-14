// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Admin;

/// <summary>The creation response: the only place the plaintext token is ever disclosed.</summary>
public sealed record CreateUploadCredentialResult(UploadCredentialProjection Credential, String Token);
