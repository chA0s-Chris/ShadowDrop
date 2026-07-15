// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

internal sealed record UploadCredentialSecretHash(String HashBase64, String SaltBase64, Int32 Iterations, Int32 Version);
