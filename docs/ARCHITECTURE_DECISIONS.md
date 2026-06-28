# Architecture Decisions

## ADR-2026-05-17-01: File-scoped direct-HTTP crypto for issue #14

**Status:** Accepted

**Context:** The original direct-HTTP implementation path bound decryption to share-scoped values, but uploaded files are encrypted before any share exists. That made normal uploads incompatible with direct-HTTP downloads.

**Decision:** For the MVP, direct-HTTP cryptographic binding is file-scoped. The implementation removes `shareId` from the content-key derivation inputs and from authenticated chunk metadata, leaving file-level values as the source of truth for decryption. To make client-side upload encryption use that same file-scoped context safely, the server now reserves the opaque `fileId` before upload and persists that reservation until the encrypted blob metadata is committed.

**Consequences:**
- Direct-HTTP now works with normal uploads because encryption depends only on upload-time-known values.
- Upload clients can derive file-scoped crypto before sending ciphertext because the server-issued `fileId` exists before encryption begins.
- Multiple shares of the same uploaded file intentionally reuse the same file-scoped cryptographic context in the MVP.
- If stronger per-share isolation is needed later, the next step is a stored upload-time encryption context rather than returning to share-derived binding.

**Related artifacts:**
- `ai-plans/0014-basic-download-endpoint.md`
- `ai-plans/0014-basic-download-endpoint.decision-options.md`
- `PROJECT_CONCEPT.md`

## ADR-2026-06-28-01: Single-use uploaded files for shares for issue #85

**Status:** Accepted

**Context:** Uploaded files are encrypted and persisted before any share exists, and share creation previously only de-duplicated file ids within a single request. A completed uploaded file id could therefore be referenced by multiple share records. That makes the expired/revoked share cleanup workflow (issue #79) unsafe: deleting the blob for one expired or revoked share could break another active share that references the same file, and it would force cleanup to maintain cross-share blob reference counting.

**Decision:** Uploaded files are single-use across shares for the MVP — a completed uploaded file id may be referenced by at most one share record. Enforcement lives in the share repository's create path rather than in `CreateShareService`, so the cross-share reference check and the insert of the new `ShareRecord` happen atomically under `LiteDbShareMetadataRepository`'s `_syncRoot` lock and existing transaction, avoiding a check-then-insert race. A non-unique multikey LiteDB index over the share file entries' `FileId` backs the lookup; LiteDB 5 does not support unique multikey indexes, so uniqueness is enforced by repository logic rather than a database unique constraint. A duplicate reference (in any `CleanupState`, including expired or revoked shares) is rejected with `CreateShareValidationException`, surfaced by the admin endpoint as a `400 Bad Request`.

**Consequences:**
- Cleanup (issue #79) can delete a share's blobs without cross-share reference counting and without risk of breaking another active share.
- This supersedes the ADR-2026-05-17-01 consequence "Multiple shares of the same uploaded file intentionally reuse the same file-scoped cryptographic context in the MVP": a file can now belong to at most one share, so that reuse no longer occurs.
- Creating a second share over an already-shared file now fails validation; callers must upload the file again to share it again.
- `IShareMetadataRepository.CreateAsync` carries an implicit rejection contract (documented on the interface) that any future repository implementation must uphold.

**Related artifacts:**
- `ai-plans/0085-enforce-single-use-uploaded-files-for-shares.md`
- `ai-plans/0079-add-expired-and-revoked-share-cleanup.md`
