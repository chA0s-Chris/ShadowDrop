// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareRevocationService(IShareMetadataRepository shareMetadataRepository, TimeProvider timeProvider)
{
    public async Task<Boolean> RevokeAsync(Guid shareId, CancellationToken cancellationToken)
    {
        if (shareId == Guid.Empty)
        {
            return false;
        }

        return await shareMetadataRepository.TryRevokeAsync(shareId, timeProvider.GetUtcNow(), cancellationToken);
    }
}
