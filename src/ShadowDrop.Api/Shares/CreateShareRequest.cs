// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed record CreateShareRequest(
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<CreateShareFileRequest>? Files,
    Boolean? DirectHttpEnabled = null,
    Boolean? GenerateDownloadBearerToken = null,
    DateTimeOffset? DownloadBearerTokenExpiresAtUtc = null);

public sealed record CreateShareFileRequest(Guid FileId, String? DisplayName = null);
