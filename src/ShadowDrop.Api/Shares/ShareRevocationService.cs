// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareRevocationService(
    IShareMetadataRepository shareMetadataRepository,
    TimeProvider timeProvider,
    ILogger<ShareRevocationService> logger)
{
    public async Task<Boolean> RevokeAsync(Guid shareId, CancellationToken cancellationToken)
    {
        if (shareId == Guid.Empty)
        {
            return false;
        }

        var revoked = await shareMetadataRepository.TryRevokeAsync(shareId, timeProvider.GetUtcNow(), cancellationToken);
        if (revoked)
        {
            logger.LogInformation("Share revoked. ShareId: {ShareId}", shareId);
        }
        else
        {
            logger.LogWarning("Share revocation rejected because the share was not found. ShareId: {ShareId}", shareId);
        }

        return revoked;
    }
}
