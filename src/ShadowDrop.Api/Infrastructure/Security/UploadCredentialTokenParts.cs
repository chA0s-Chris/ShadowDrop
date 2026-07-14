// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

/// <summary>
/// A freshly generated plaintext token together with its parts. Never persisted or logged; the token text is
/// returned to the caller exactly once at creation time.
/// </summary>
public sealed record UploadCredentialTokenParts(String Token, String Selector, String Secret);
