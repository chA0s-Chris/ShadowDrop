## Rationale

Back the upload slice with the MVP persistence model so encrypted files can be stored durably and discovered later for
sharing and download. This should establish the first local filesystem storage backend and LiteDB-backed metadata
repository without leaking backend details into higher-level workflows.

## Acceptance Criteria

- [x] A blob storage abstraction exists for encrypted file content.
- [x] A local filesystem blob storage implementation exists and uses the configured local storage root.
- [x] A metadata persistence abstraction exists for uploaded-file records.
- [x] A LiteDB-backed metadata implementation persists uploaded-file records.
- [x] Uploaded-file metadata includes file id, storage path or blob key, original file name, plaintext length, encrypted
  length, content type if known, encryption format version, algorithm id, chunk size, chunk count, per-share KDF salt,
  and optional plaintext SHA-256. Metadata may store non-secret encryption descriptors and salts (treated as contract
  data whose actual use belongs to later share creation), but never actual file encryption keys.
- [x] Storage and metadata writes are coordinated so failed uploads do not leave committed metadata pointing to missing
  blobs.
- [x] Stored blob content is encrypted content only.
- [x] Local blob files and the LiteDB metadata file are stored with restrictive filesystem permissions (0600 or
  equivalent) appropriate for server-only access.
- [x] Storage paths do not trust user-supplied file names for filesystem layout.
- [x] Automated tests cover successful persistence, retrieval of uploaded-file metadata, and failure handling around
  partial writes.

## Technical Details

Implement small abstractions in `ShadowDrop.Api` for blob storage and uploaded-file metadata persistence. Keep them
narrow and workflow-oriented so later S3-compatible storage or alternate metadata repositories can be added without
rewriting feature slices.

The local filesystem backend should write encrypted blobs under the configured storage root using server-generated paths
derived from file ids or similarly opaque identifiers. Do not make storage layout depend on original file names.
Directory creation and path handling should be safe for one-container deployments and future cleanup work.

The LiteDB implementation should introduce the first server-only document shape for uploaded files. Keep persistence
entities inside the API project rather than moving them into `ShadowDrop.Shared`. Store only metadata needed for later
share creation and download; do not introduce share, token, or audit persistence beyond what this slice directly needs.

The upload slice from the previous plan should use these abstractions rather than writing directly to LiteDB or the
filesystem. Failure handling should prefer not committing metadata when blob persistence fails, and should
deterministically delete or quarantine partially written local files rather than cleaning them up only where practical.
Tests may use isolated on-disk paths and dedicated LiteDB files per run.

**Intentional Deferral:** Blob-level integrity verification (e.g., checksum validation on read) is deferred from this
slice and should be added in a separate initiative.
