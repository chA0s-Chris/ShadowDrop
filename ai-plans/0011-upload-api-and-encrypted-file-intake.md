## Rationale

Implement the first real sender workflow by accepting encrypted file uploads through the protected API. This should
establish the upload contract and intake pipeline without yet committing to the final storage and metadata backend
abstraction details beyond what this slice needs.

## Acceptance Criteria

- [ ] A protected upload endpoint exists for encrypted file intake.
- [ ] Upload requests require `Authorization: Bearer <admin-token>`.
- [ ] The upload contract carries only encrypted file content and non-secret file metadata.
- [ ] The API rejects uploads that attempt to send plaintext-only file metadata without encryption metadata.
- [ ] The upload pipeline supports streaming request bodies instead of buffering full files in memory.
- [ ] Upload metadata includes share-independent file identity, original file name, plaintext length, encrypted length,
  encryption format version, algorithm id, chunk size, chunk count, per-share KDF salt, and optional plaintext SHA-256.
- [ ] The API does not persist or log plaintext share secrets, derived file keys, `ShadowDrop-Key`, or `sd-key` values.
- [ ] Upload validation rejects malformed lengths, invalid chunk counts, invalid salts, and inconsistent encryption
  metadata.
- [ ] A successful upload returns the created file id and persisted file metadata needed for later share creation.
- [ ] Automated tests cover authorized upload success, unauthorized upload rejection, and validation failures through
  real HTTP requests.

## Technical Details

Implement this as a vertical slice in `ShadowDrop.Api` using Minimal APIs. The upload endpoint should live under the
protected upload/management route group established by the walking skeleton.

The request should represent an already encrypted file. The client remains responsible for encryption; the API only
accepts ciphertext plus non-secret metadata needed for later download and range handling. Reuse shared contracts from
`ShadowDrop.Shared` where that keeps the wire contract stable, but keep server-only persistence concerns inside the API
project.

Design the endpoint and service flow around streaming. The API should read from the request stream and hand the
encrypted bytes to the storage/persistence path without loading the whole file into memory. Validation should happen
before or during streaming where practical, and failure paths should not leave partially accepted metadata in a
committed state.

Do not accept plaintext share secrets, direct-download key material, or download bearer tokens in this slice. The upload
workflow is only about storing encrypted blobs and the metadata required for later share creation and download. Logging
and audit behavior must avoid secrets and must not record query strings or headers that could contain key material in
later slices.
