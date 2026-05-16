## Rationale

Deliver the first recipient-facing vertical slice by serving downloadable files through a public HTTP endpoint. This
initial version should validate share access rules and stream a full-file response before range support is added.

## Acceptance Criteria

- [ ] A public download endpoint exists for share files.
- [ ] The endpoint can resolve a share by presented share token and file id.
- [ ] The endpoint denies expired shares by validating `expiration_timestamp < now` at download time. Expiration is
  soft (checked on each request); no cleanup jobs or active background revocation required.
- [ ] The endpoint validates an optional download bearer token from `Authorization: Bearer <token>` when required.
- [ ] The endpoint does not accept download bearer tokens through query parameters.
- [ ] The endpoint supports both direct-HTTP mode and CLI decrypt mode at the contract level.
- [ ] In direct-HTTP mode, the endpoint accepts key material through the `ShadowDrop-Key` header and `sd-key` query
  parameter.
- [ ] In CLI decrypt mode, the endpoint streams encrypted content without requiring the server to receive the decryption
  key.
- [ ] Successful responses include appropriate filename and content-length headers for the selected file.
- [ ] The endpoint streams responses instead of buffering the full file in memory.
- [ ] Automated tests cover full-file download success, missing token rejection, expired share rejection, and required
  download-token rejection.

## Technical Details

Use the public download route group established by the walking skeleton. Prefer the file-specific route shape from the
concept, such as `GET /d/{token}/files/{fileId}`, so multi-file shares remain individually addressable from the start.

This slice should focus on authorization and streaming behavior for whole-file downloads. For direct-HTTP mode, the
server may receive key material and decrypt before writing the response. For CLI decrypt mode, the server should return
encrypted bytes and the client will decrypt later. Keep these modes explicit in the code so range support can build on
them rather than retrofitting behavior.

Do not add range processing in this plan. Whole-file responses are enough here as long as the code structure leaves room
for later byte-range handling. Audit and logging metadata may include safe identifiers (share id, file id, request
outcome) and high-level success/failure results, but must not include token hashes, Authorization header content,
ShadowDrop-Key header content, or plaintext key material. If audit events are added, keep them minimal and aligned with
the concept.
