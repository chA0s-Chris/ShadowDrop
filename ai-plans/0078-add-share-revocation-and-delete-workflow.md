# Add Share Revocation and Delete Workflow

> Issue: [#78](https://github.com/chA0s-Chris/ShadowDrop/issues/78)

## Rationale

ShadowDrop already stores `RevokedAtUtc` on shares, and the public download path treats revoked shares as invalid. The missing MVP slice is an operator-facing workflow to revoke or delete a share without exposing stored token hashes or other secrets. This plan adds the protected admin API and scriptable CLI path needed to mark shares revoked and make revocation an explicit lifecycle boundary.

## Acceptance Criteria

- [ ] A protected admin API endpoint exists to revoke a share by internal share id.
- [ ] Revoking a share requires `Authorization: Bearer <admin-token>` and respects `ShadowDrop:ApiExposure:EnableAdminOperations`.
- [ ] Revocation persists `RevokedAtUtc` without exposing stored token hashes or other secrets.
- [ ] Public manifest and file download requests for a revoked share fail with the same non-enumerating behavior used for invalid shares.
- [ ] The CLI exposes `shadowdrop share revoke <share-id>`; a `share delete` alias is optional and must behave as logical revocation.
- [ ] Revoking an unknown share id returns HTTP 404, and the CLI surfaces this as a distinct failure.
- [ ] The CLI command returns a non-zero exit code on failure and zero on success, and prints no admin/upload tokens or stored secret material.
- [ ] Automated tests cover API authorization, successful revocation, repeated/idempotent revocation behavior, unknown-share-id (404) handling, revoked download denial, and CLI command behavior.

## Technical Details

Add the write-side revocation path across the repository, service, API, and CLI layers. `ShareRecord.RevokedAtUtc` and the existing download-side rejection in `DownloadFileService.TryResolveShareAsync` should be reused rather than introducing a separate revocation state. Extend the metadata repository with an idempotent update method that marks a share revoked by internal share id and does not return or log token hashes, bearer tokens, upload tokens, key material, or `sd-key` URLs.

Expose the operation through a protected admin endpoint, preferably `POST /api/admin/shares/{shareId}/revoke`, that follows the existing admin authorization and API exposure patterns. The endpoint should require `Authorization: Bearer <admin-token>`, be disabled when `ShadowDrop:ApiExposure:EnableAdminOperations` is false, and use the same non-secret response style as other administrative operations. Revoking an unknown share id returns HTTP 404.

Add a CLI command for scriptable use, preferring `shadowdrop share revoke <share-id>` as the primary command. The revoke command should reuse the existing upload/admin token resolution used by share creation, accepting the existing `--upload-token` option, `SHADOWDROP_UPLOAD_TOKEN` environment variable, and `uploadToken` config value as the admin bearer token for this MVP. If a `delete` alias is added for MVP, document and implement it as logical revocation rather than hard deletion. CLI success and failure output should be clear for automation and must not print admin/upload tokens or stored secret material.

Keep repeated revocation idempotent: revoking an already revoked share should not fail because the share is already revoked, and it should leave metadata consistent for the later cleanup workflow.
