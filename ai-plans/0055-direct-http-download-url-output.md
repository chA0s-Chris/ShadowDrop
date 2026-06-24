## Rationale

Direct HTTP uploads should produce output that can be shared or passed to `curl` immediately. Today,
`shadowdrop upload --direct-http` prints `share-url:<server>/d/<share-token>`, but that endpoint is the JSON manifest,
not the file download endpoint. Add explicit direct download URL output for direct HTTP shares while preserving existing
metadata output where useful. This plan addresses
[#55](https://github.com/chA0s-Chris/ShadowDrop/issues/55).

## Acceptance Criteria

- [x] `shadowdrop upload --direct-http` emits direct file download URLs that point at
  `/d/<share-token>/files/<file-id>?sd-key=<base64-key-material>`.
- [x] The `sd-key` query value is URL-encoded so a base64 key containing `+`, `/`, or `=` survives intact and the URL can
  be used directly with `curl` or a browser.
- [x] When exactly one file is uploaded, a direct HTTP upload emits one simple, parseable `download-url:<url>` line.
- [x] When more than one file is uploaded, a direct HTTP upload emits one deterministic, parseable
  `download-url:<file-id>:<url>` line for every uploaded file.
- [x] Existing `share-url:` output remains available as the manifest URL so existing scripts do not break.
- [x] Non-direct-HTTP upload output remains unchanged.
- [x] `shadowdrop upload --direct-http --secrets-out <file>` is rejected with a validation error, because a direct HTTP
  download URL embeds the key material and therefore has no secret to redirect to a file.
- [x] `--json` output includes direct HTTP download URLs as
  `directHttpDownloads: [{ fileId, fileName, downloadUrl }]` for direct HTTP shares without removing existing fields.
- [x] Non-direct-HTTP `--json` output omits `directHttpDownloads`.
- [x] Automated tests cover single-file direct HTTP output, multiple-file direct HTTP output, JSON output, the rejected
  `--direct-http --secrets-out` combination, and unchanged separate-key output.

## Technical Details

Extend the CLI upload result model and output writer path so direct HTTP shares can report both the existing manifest
URL and one file download URL per uploaded file. The direct URLs should be built from the same server URL and share token
used for `share-url`, combined with each uploaded file ID through the existing download URI helper
(`ShareDownloadUriFactory.CreateFileUri`), then extended with the browser-compatible direct HTTP key query parameter
`sd-key=<base64-key-material>`.

Note that the direct HTTP download endpoint base64-decodes the presented key material
(`DownloadFileService.WithDecodedDirectHttpKeyMaterialAsync` calls `Convert.FromBase64String`), whereas the upload flow
currently carries the share secret only as hex (`ShareSecretHex`, produced via `Convert.ToHexStringLower`). The `sd-key`
value must therefore be the base64 form of the same key material — derive it from the existing hex secret
(`Convert.FromHexString` then `Convert.ToBase64String`), or extend the executor result to expose the base64 form
directly. The base64 must be URL-encoded when placed in the query string so `+`, `/`, and `=` are not corrupted.

Reject the `--direct-http --secrets-out` combination during option validation, alongside the existing direct-HTTP
exclusions (`--queue-out`, `--generate-download-token`). A direct HTTP download URL embeds the key material, so there is
no separable secret to redirect to a `--secrets-out` file. Plain `--json` (without `--secrets-out`) is unaffected:
selecting JSON is itself an output destination, so the embedded-key download URLs belong there.

For plain text output, keep `share-url:` as the manifest URL for compatibility. Emit `download-url:<url>` for the common
single-file direct HTTP case. For multiple files, emit repeated `download-url:<file-id>:<url>` lines. Keep the ordering
aligned with the uploaded files/result order.

For JSON output, add a `directHttpDownloads` collection for direct HTTP shares while retaining existing fields such as
`shareUrl`, `uploadedFileIds`, and `credentials`. Each entry should include `fileId`, `fileName`, and `downloadUrl`.
Omit `directHttpDownloads` for non-direct-HTTP shares. Because the CLI serializes with source-generated, Native-AOT-safe
JSON, ensure the new entry type is reachable from `CliJsonSerializerContext` (transitively via `UploadCommandResult`, or
registered explicitly) so AOT serialization does not break at runtime.

Update upload command tests around `--direct-http` so they assert the emitted direct file URL targets the file endpoint,
not the manifest endpoint. Also add regression coverage proving ordinary separate-key uploads still emit the same
`share-url` and `share-key` output they do today.
