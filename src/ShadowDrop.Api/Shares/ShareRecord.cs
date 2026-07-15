// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed record ShareRecord(
    Guid ShareId,
    String ShareTokenHashBase64,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    ShareCleanupState CleanupState,
    Boolean DirectHttpEnabled,
    DownloadBearerTokenRecord? DownloadBearerToken,
    IReadOnlyList<ShareFileEntryRecord> Files,
    Guid? OwnerCredentialId = null);
