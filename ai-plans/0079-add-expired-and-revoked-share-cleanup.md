# Add expired and revoked share cleanup

> Issue: [#79](https://github.com/chA0s-Chris/ShadowDrop/issues/79)

## Rationale

Share expiration and revocation currently block access at request time, but the MVP retention model also treats them as boundaries for stored file cleanup. The goal is to make expired and revoked shares discoverable by an operator-safe cleanup workflow that removes associated local blobs, records durable cleanup progress, and can be run repeatedly without leaking sensitive token or key material.

## Acceptance Criteria

- [ ] Expired shares are discoverable for cleanup without requiring a download request to touch them first.
- [ ] Revoked shares are discoverable for cleanup after the revocation/delete workflow marks them revoked.
- [ ] Cleanup resolves each share file through upload metadata, deletes the associated local blob files, and leaves uploaded-file metadata intact for audit/idempotency.
- [ ] Cleanup updates share metadata consistently using durable `Pending`, `Completed`, and `Failed` cleanup states so repeated cleanup runs are idempotent and failed runs can be retried.
- [ ] Cleanup behavior does not expose plaintext share tokens, bearer tokens, key material, or `sd-key` URLs in logs.
- [ ] The API runs cleanup once after startup and periodically while running, with the periodic schedule configured by an environment-backed cron expression that defaults to once every two hours.
- [ ] A protected admin endpoint and `shadowdrop share cleanup` CLI command can trigger cleanup manually and report summary counts.
- [ ] Automated tests cover expired-share cleanup, revoked-share cleanup, missing/already-deleted blob handling, idempotency, scheduling configuration, manual trigger behavior, and failure handling.

## Technical Details

Build cleanup around the existing `ShareRecord.CleanupState` concept rather than adding a parallel lifecycle model. Extend the state from the current `Pending` value to `Pending`, `Completed`, and `Failed`. A newly created share starts as `Pending`; a cleanup pass moves it to `Completed` only after all associated blob deletes have succeeded or the blobs are already absent; a storage or metadata failure leaves or moves it to `Failed`, and later cleanup passes should treat both `Pending` and `Failed` expired/revoked shares as retryable. Do not add a durable `InProgress` state for the MVP, because a one-container process crash could otherwise leave records stuck in an intermediate state.

This cleanup workflow depends on uploaded files being single-use for shares, which is handled separately in [#85](https://github.com/chA0s-Chris/ShadowDrop/issues/85). Single-use enforcement ensures a completed uploaded file id is referenced by at most one share record, so cleanup can delete a share's blobs without cross-share reference counting and without risk of breaking another active share. Treat single-use enforcement as a precondition: cleanup should land on top of it rather than re-deriving cross-share blob ownership here.

Extend `IShareMetadataRepository` with cleanup-oriented queries and updates. The repository should be able to find shares whose `ExpiresAtUtc` is in the past and shares whose `RevokedAtUtc` is set, filtering to records whose cleanup state is not `Completed`. Add an idempotent metadata update path for cleanup results so repeated runs do not fail after a previous successful or partially successful pass.

Implement the cleanup operation as a service that coordinates share metadata, upload metadata, and blob storage. It should enumerate cleanup candidates, resolve each `ShareFileEntryRecord.FileId` through `IUploadedFileMetadataRepository.GetAsync`, delete the uploaded file's `BlobKey` through `IBlobStorage.DeleteIfExistsAsync`, and then update the share metadata to reflect the durable cleanup result. Missing blobs should be treated as an idempotent success for that blob. Uploaded-file metadata should remain in place after blob deletion for MVP auditability and repeatable state transitions; if upload metadata is unexpectedly missing, treat that as a cleanup failure that can be retried or investigated rather than silently marking the share complete. Storage or metadata failures should leave enough state for a later run to retry safely. Log share ids, file ids, and operational status only; do not log plaintext share tokens, bearer tokens, key material, upload tokens, stored token hashes,
or `sd-key` URLs.

Run cleanup automatically inside the API process using the same cleanup service. Implement this as a `BackgroundService` whose `ExecuteAsync` runs one cleanup pass shortly after startup and then loops: each iteration computes the next run time from a configured cron expression and waits until then before invoking the cleanup service. Parse the cron expression with the [Cronos](https://www.nuget.org/packages/Cronos) package (lightweight, time-zone/DST aware) using its next-occurrence API. Bind the cron expression through the existing options pattern — add a `CleanupOptions` class hung off `ShadowDropOptions` and wired through `ShadowDropOptionsBinding`, exposing the schedule via normal environment-backed configuration (`ShadowDrop:Cleanup:...`). The default cron expression should represent once every two hours (`0 */2 * * *`).

The deployment is single-instance (one container), so overlap avoidance is handled in-process rather than with a distributed lock. The `BackgroundService` loop is inherently sequential, so scheduled passes cannot overlap themselves; guard against a manual trigger overlapping a scheduled pass (and vice versa) with an in-process skip-if-running guard such as a shared `SemaphoreSlim` that the manual entry points and the scheduled loop both honor.

Expose a protected manual trigger through the admin surface, for example `POST /api/admin/shares/cleanup`, using the same admin bearer token and `ShadowDrop:ApiExposure:EnableAdminOperations` behavior as other admin operations. Add `shadowdrop share cleanup` as a thin CLI wrapper over that endpoint, reusing the existing server URL and admin/upload-token resolution conventions. Manual trigger responses and CLI output should report summary counts such as candidates scanned, shares completed, blobs deleted, already-missing blobs, and failures, without exposing secret material.

Automated tests should cover the cleanup service and repository behavior directly, the hosted startup/periodic trigger behavior, and the manual operator entry points. Include expired-share discovery, revoked-share discovery, successful blob deletion, missing/already-deleted blobs, repeated cleanup runs, partial failure handling, metadata state transitions, cron configuration and defaulting, overlap-guard behavior, endpoint/CLI authorization, output summary counts, and log/output assertions that sensitive values are not emitted.
