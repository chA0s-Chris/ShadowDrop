## Rationale

Add the first share-management workflow so uploaded files can be turned into temporary download capabilities. This slice
should establish share records, expiration semantics, high-entropy share tokens, and optional hashed download bearer
tokens without yet implementing the download endpoint itself.

## Acceptance Criteria

- [x] A protected share-creation endpoint exists.
- [x] Share creation requires `Authorization: Bearer <admin-token>`.
- [x] A share can reference one or more previously uploaded files.
- [x] A share stores creation timestamp, expiration timestamp, optional revocation timestamp, cleanup state, direct-HTTP
  mode flag, and file entries.
- [x] Share creation generates an opaque high-entropy share token with at least 256 bits of entropy (32 bytes).
- [x] The metadata store persists only a cryptographic hash of the share token, not the plaintext token.
- [x] Plaintext share tokens are returned only once in the creation response and never logged or persisted server-side
  thereafter.
- [x] Share creation can optionally require a download bearer token, also with at least 256 bits of entropy.
- [x] Optional download bearer tokens are stored only as cryptographic hashes with their own expiration timestamp.
- [x] Download bearer token hashes must never be persisted in plaintext form or included in any server log.
- [x] Direct HTTP mode is explicit opt-in per share and defaults to disabled.
- [x] Share creation supports uploader-controlled display name overrides per file.
- [x] Share creation rejects missing files, duplicate file ids, invalid expiration values, and invalid direct-HTTP or
  token combinations without exposing token-shape details in error responses.
- [x] Invalid mode/token combinations are explicitly rejected: direct HTTP mode with a download bearer token, or
  separate-key mode without optional token setup.
- [x] The response returns the share id and the plaintext tokens only at creation time; client must store tokens as they
  are never retrievable afterward.
- [x] All share metadata, file entries, and token hashes are persisted atomically; partial failures in any persistence
  layer trigger full rollback with no orphaned state.
- [x] Revocation-related and cleanup-state fields (revocation timestamp, cleanup state) are initialized at creation time
  and persisted, though revocation enforcement belongs to later slices. Cleanup state is a named enum value, not a
  boolean flag: initialized to `"PENDING"` (awaiting cleanup), not a boolean default.
- [x] Expiration validation is deferred to token-validation time (later slices); this slice only persists expiration
  timestamps as part of share metadata.
- [x] Automated tests cover share creation success, hashed token persistence, expiration timestamp storage,
  invalid-request rejection, atomic persistence, and plaintext token confidentiality.

## Technical Details

Implement this as a new protected management slice in `ShadowDrop.Api`. The slice should operate on previously
uploaded-file metadata and create the server-side records required for later download authorization.

Keep token handling deliberately simple and aligned with the concept. Generate strong random share tokens and optional
download bearer tokens with at least 256 bits of cryptographic entropy (minimum 32 bytes of random data). Return both
plaintext tokens **once** in the creation response only; plaintext tokens must **never** be persisted, logged, or
included
in any server-side record after the response is sent. Persist only protected token material (SHA-256 hash of
plaintext tokens) so validation can occur later without exposing original values. The implementation should make it easy
to
validate tokens later without ever storing plaintext token values or leaking token structure in error responses.

Model expiration separately for shares and optional download tokens. A share expiration timestamp controls the overall
lifecycle boundary. A download bearer token may expire earlier than the share. This slice persists revocation timestamp
(nullable, null until revoked) and cleanup state as a named enum value (initialized to `"PENDING"`) at share creation
time, establishing the
foundation for later revocation and cleanup endpoints. Cleanup state is not a boolean flag but a discrete named state
enabling future extensibility for cleanup job orchestration. Expiration validation (checking if a share or token has
expired)
belongs to token-validation endpoints in later slices, not here; this slice only stores expiration timestamps as
metadata.
This slice does not implement revocation endpoints or background cleanup jobs.

Do not support browser-style bearer-token transport through query parameters. If a share requires a download bearer
token, that token is for header-based clients such as the CLI or `curl` only. Direct HTTP mode and separate-key mode
should
both be represented in share metadata, with separate-key mode as the default. Invalid mode/token combinations must be
explicitly rejected:

- **Direct HTTP mode + optional download bearer token:** Invalid. Direct HTTP shares have no authentication; tokens
  cannot be enforced.
- **Separate-key mode without optional bearer token setup:** Invalid. If separate-key mode is selected, an optional
  download bearer token **must** be configured (required or optional availability).

Error responses for invalid mode/token combinations must not expose the constraint logic or token-shape details; use
generic HTTP 400 (Bad Request) with a minimal public message only.
