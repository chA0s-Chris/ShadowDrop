## Rationale

Add the first share-management workflow so uploaded files can be turned into temporary download capabilities. This slice
should establish share records, expiration semantics, high-entropy share tokens, and optional hashed download bearer
tokens without yet implementing the download endpoint itself.

## Acceptance Criteria

- [ ] A protected share-creation endpoint exists.
- [ ] Share creation requires `Authorization: Bearer <admin-token>`.
- [ ] A share can reference one or more previously uploaded files.
- [ ] A share stores creation timestamp, expiration timestamp, optional revocation timestamp, cleanup state, direct-HTTP
  mode flag, and file entries.
- [ ] Share creation generates an opaque high-entropy share token.
- [ ] The metadata store persists only a cryptographic hash of the share token, not the plaintext token.
- [ ] Share creation can optionally require a download bearer token.
- [ ] Optional download bearer tokens are stored only as cryptographic hashes with their own expiration timestamp.
- [ ] Direct HTTP mode is explicit opt-in per share and defaults to disabled.
- [ ] Share creation supports uploader-controlled display name overrides per file.
- [ ] Share creation rejects missing files, duplicate file ids, invalid expiration values, and invalid direct-HTTP or
  token combinations.
- [ ] The response returns the share id and the plaintext tokens only at creation time.
- [ ] Automated tests cover share creation success, hashed token persistence, expiration validation, and invalid
  requests.

## Technical Details

Implement this as a new protected management slice in `ShadowDrop.Api`. The slice should operate on previously
uploaded-file metadata and create the server-side records required for later download authorization.

Keep token handling deliberately simple and aligned with the concept. Generate strong random share tokens and optional
download bearer tokens, return them once in the creation response, and persist only protected token material such as a
cryptographic hash. The implementation should make it easy to validate tokens later without ever storing plaintext token
values.

Model expiration separately for shares and optional download tokens. A share expiration timestamp controls the overall
lifecycle boundary. A download bearer token may expire earlier than the share. This slice does not need revocation
endpoints yet, but it should persist the fields needed for later revocation and cleanup.

Do not support browser-style bearer-token transport through query parameters. If a share requires a download bearer
token, that token is for header-based clients such as the CLI or `curl`. Direct HTTP mode and separate-key mode should
both be represented in share metadata, with separate-key mode as the default.
