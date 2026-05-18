// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public interface IUploadedFileMetadataRepository
{
    Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Boolean> HasActiveReservationAsync(Guid fileId, CancellationToken cancellationToken);

    Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken);

    Task SaveAsync(UploadedFileRecord record, CancellationToken cancellationToken);

    Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken);
}
