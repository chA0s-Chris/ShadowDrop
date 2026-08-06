// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public interface IUploadedFileMetadataRepository
{
    Task<Int32> GetActivePendingReservationCountAsync(CancellationToken cancellationToken);

    Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadedFileListProjection>> GetListProjectionsAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Administrative file-list projections are not supported by this repository.");

    Task<UploadedFileStorageStats> GetStorageStatsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns at most <paramref name="limit"/> completed uploads that are candidates for unreferenced-upload
    /// reclamation: never reservations, and either carrying no completion timestamp or one at or before
    /// <paramref name="completionCutoffUtc"/>. Never-inspected candidates come first, then the least recently
    /// inspected, with completion time and file identifier as deterministic tie-breakers, so a candidate that is
    /// repeatedly skipped cannot starve the backlog.
    /// </summary>
    Task<IReadOnlyList<UploadSweepCandidate>> GetSweepCandidatesAsync(
        DateTimeOffset completionCutoffUtc,
        Int32 limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Unreferenced-upload sweep candidates are not supported by this repository.");

    Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken);

    Task<Guid> ReserveFileIdAsync(Guid ownerCredentialId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Owner-bound upload reservations are not supported by this repository.");

    Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken);

    Task<Boolean> TryClaimReservationAsync(Guid fileId, Guid ownerCredentialId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Owner-bound reservation claims are not supported by this repository.");

    Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord record, CancellationToken cancellationToken);

    Task<Boolean> TryDeleteAsync(Guid fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Uploaded-file metadata deletion is not supported by this repository.");

    Task<Boolean> TryMarkBlobDeletedAsync(Guid fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Retained-blob accounting transitions are not supported by this repository.");

    /// <summary>
    /// Records that the unreferenced-upload sweep inspected <paramref name="fileId"/>, rotating it to the back of
    /// the candidate queue. A completed upload that carries no completion timestamp is stamped with
    /// <paramref name="inspectedAtUtc"/> here, so a legacy record waits a full grace period from its first
    /// inspection instead of being reclaimed immediately.
    /// </summary>
    Task<Boolean> TryRecordSweepInspectionAsync(
        Guid fileId,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Unreferenced-upload sweep inspection state is not supported by this repository.");
}
