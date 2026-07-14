// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

public sealed record UploadCredentialPage(IReadOnlyList<UploadCredentialRecord> Credentials, UploadCredentialListCursor? NextCursor);
