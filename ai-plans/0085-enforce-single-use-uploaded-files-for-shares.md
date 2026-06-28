# Enforce single-use uploaded files for shares

> Issue: [#85](https://github.com/chA0s-Chris/ShadowDrop/issues/85)

## Rationale

Uploaded files must become single-use for shares. Today `CreateShareService` only deduplicates file ids *within a single create request*, so a completed uploaded file id can still be referenced by multiple share records. That makes blob deletion during expired/revoked share cleanup unsafe, because removing a blob for one share could break another active share. Enforcing single-use removes the need for cross-share blob reference counting in cleanup and is a precondition for the cleanup workflow in [#79](https://github.com/chA0s-Chris/ShadowDrop/issues/79).

## Acceptance Criteria

- [x] Share creation rejects any completed uploaded file id that is already referenced by an existing share record, regardless of that share's cleanup state.
- [x] Rejection surfaces through the existing share-creation validation/failure path (a `CreateShareValidationException`, the same mechanism used when a referenced file does not exist).
- [x] Automated tests cover cross-share single-use enforcement and confirm the existing within-request duplicate rejection still holds.

## Technical Details

Back cross-share single-use enforcement with an atomic repository check-and-insert path. Add a non-unique multikey LiteDB index over the share file entries' `FileId` values, alongside the existing `ShareId`/`ShareTokenHashBase64` indexes in the `LiteDbShareMetadataRepository` constructor, so the cross-share lookup is index-backed. LiteDB 5 does not support unique multikey indexes, so uniqueness must be enforced by repository logic rather than a database unique constraint.

Keep the existing `CreateShareService.CreateAsync` validation flow for request shape, within-request duplicate file ids, and `IUploadedFileMetadataRepository.GetAsync` existence checks. Move the cross-share duplicate-reference check into the share repository's create path so checking all requested file ids and inserting the new `ShareRecord` happen under `LiteDbShareMetadataRepository`'s `_syncRoot` and existing transaction. If any requested file id is already present in another share document, including expired or revoked shares in any `CleanupState`, throw a `CreateShareValidationException` from the share-creation path before inserting the new share; otherwise persist the share normally.
