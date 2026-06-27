## Rationale

Follow-up to [#55](https://github.com/chA0s-Chris/ShadowDrop/issues/55). Direct HTTP shares currently expose only a
`download-url:` line whose key material rides in the `sd-key` query parameter. Query-string keys leak into server access
logs, proxy/CDN logs, browser history, and `Referer` headers — a risk `PROJECT_CONCEPT.md` explicitly calls out. The
API already accepts the same key via the `ShadowDrop-Key` HTTP header. The direct-HTTP endpoint requires *exactly one*
key source — `DownloadFileService` rejects a request that carries both the header and the `sd-key` query parameter — so
the logging-safe path is to pass the key via the header and target the file URL **without** `sd-key`. No server change is
required. This plan adds a ready-to-run, header-based `curl` command to the direct-HTTP upload output (alongside the
existing browser-friendly `sd-key` URL), so the key can be passed out of the request URL. This plan addresses
[#56](https://github.com/chA0s-Chris/ShadowDrop/issues/56).

## Acceptance Criteria

- [x] For a direct HTTP upload, in addition to the existing `download-url:` line, the CLI emits a ready-to-run
  header-based `curl` command that passes the key via the `ShadowDrop-Key` header (not the URL) and targets the file
  endpoint `/d/<share-token>/files/<file-id>` with no `sd-key` query parameter.
- [x] When exactly one file is uploaded, the command is emitted as a single `curl-command:<command>` line.
- [x] When more than one file is uploaded, one deterministic `curl-command:<file-id>:<command>` line is emitted per
  uploaded file, ordered consistently with the existing `download-url:<file-id>:<url>` lines (consumers split on the
  first two colons; the command value may itself contain colons).
- [x] The emitted command is correctly POSIX-sh quoted: the URL and the `-o <file name>` argument survive file names that
  contain spaces, quotes, or other shell metacharacters. POSIX `sh` is the only target; the command is not expected to be
  valid for `cmd`/PowerShell.
- [x] The `-o` target uses the original uploaded file name.
- [x] The existing `download-url:` (browser-friendly `sd-key` URL) output remains available unchanged; both methods are
  emitted.
- [x] `--json` output adds a `curlCommand` field to each `directHttpDownloads[]` entry; existing fields (`fileId`,
  `fileName`, `downloadUrl`) are retained, and `directHttpDownloads` is still omitted for non-direct-HTTP shares.
- [x] Non-direct-HTTP upload output (plain text and JSON) remains unchanged.
- [x] Automated tests need to be written, covering: single-file curl-command output, multi-file curl-command format, the
  JSON `curlCommand` field, and correct POSIX quoting for file names containing spaces and shell metacharacters.

## Technical Details

The change is confined to the CLI upload output path; no API change is required.

**Command construction.** Add a small helper (sibling to `DirectHttpDownloadUrlFactory`, e.g.
`DirectHttpCurlCommandFactory`) that builds the header-based command. It takes the same inputs already available when
`download-url:` is built — server URL, share token, file ID, the share secret (hex), and the original file name. It must:

- Derive the base64 key the same way `DirectHttpDownloadUrlFactory` does (`Convert.FromHexString` →
  `Convert.ToBase64String`), zeroing the intermediate key bytes via `CryptographicOperations.ZeroMemory` in a `finally`,
  matching the existing factory.
- Build the file endpoint URL via `ShareDownloadUriFactory.CreateFileUri` **without** the `sd-key` query parameter (the
  header carries the key instead).
- Emit the form `curl -H 'ShadowDrop-Key: <base64-key>' '<url>' -o '<file-name>'`, using
  `DownloadKeyConstants.HeaderName` for the header name rather than a literal string. This form shows command structure
  only; the actual quoting of each dynamic value follows the single-quote rule below (not the double quotes used in the
  issue example).

**POSIX quoting.** Single-quote every dynamic value (header value, URL, file name) using POSIX-sh rules: wrap in single
quotes and replace each embedded `'` with the `'\''` sequence. A single private quoting helper used for all three values
keeps it consistent. Single-quoting is preferred over the double-quoting shown in the issue example because it avoids
shell expansion of `$`, backticks, and `\` inside file names. Document in a comment that the target is POSIX `sh` only.

**Wiring into output.** In `UploadCommandHandler`, the curl command should travel with the existing
`DirectHttpDownload` records so text and JSON stay in lockstep:

- Extend the `DirectHttpDownload` record (`Results/UploadCommandResult.cs`) with a `curlCommand` property
  (`[property: JsonPropertyName("curlCommand")]`). Because it is built from the same data, it can be non-nullable and
  always populated for direct-HTTP entries; `directHttpDownloads` as a whole stays omitted for non-direct-HTTP shares, so
  no extra `JsonIgnore` is needed on the field.
- `BuildDirectHttpDownloads` populates `curlCommand` alongside `downloadUrl` for each file.
- `WriteDirectHttpDownloadsAsync` emits the new `curl-command:` lines after the `download-url:` lines, mirroring the
  single-file (`curl-command:<command>`) vs. multi-file (`curl-command:<file-id>:<command>`) shaping already used for
  `download-url:`.

**AOT JSON.** `DirectHttpDownload` is already explicitly registered in `CliJsonSerializerContext`
(`[JsonSerializable(typeof(DirectHttpDownload))]`), so adding a property requires no new context registration; the
source-generated context covers it.
