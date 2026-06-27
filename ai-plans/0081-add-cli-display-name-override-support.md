# Add CLI display-name override support

> Issue: [#81](https://github.com/chA0s-Chris/ShadowDrop/issues/81)

## Rationale

The API already supports uploader-controlled display names through `CreateShareFileRequest.DisplayName`, and the download and manifest paths use the display name when present. The CLI does not currently expose a way to set those display names, so users cannot rename files in the recipient-facing share without using the API directly.

The goal is to add a scriptable CLI contract for recipient-facing display-name overrides across both the end-to-end upload workflow and the lower-level share creation workflow, while keeping multi-file usage explicit and validation failures clear.

## Acceptance Criteria

- [ ] The CLI exposes a scriptable way to set recipient-facing display names: `upload --name <name>` for the single-file case and repeated `--display-name <path-or-file-id>=<name>` mappings for multi-file and `share create` cases.
- [ ] The end-to-end `upload` workflow supports display-name override for the single-file case.
- [ ] The lower-level `share create` workflow supports display-name overrides for previously uploaded file ids.
- [ ] Multi-file usage has an unambiguous input contract that maps each override to the intended file.
- [ ] Display names are trimmed consistently with API behavior (empty-after-trim normalizes to no display name).
- [ ] Invalid or ambiguous CLI input (duplicate, unknown, or empty-normalized mappings; single-file-only options used with multiple inputs) fails with a clear error.
- [ ] Generated `share-url`, manifest data, queue files, direct HTTP `curl-command -o`, and CLI downloads use the display name where appropriate.
- [ ] Automated tests cover single-file override, multi-file mapping, `share create`, queue output names, direct HTTP command output names, and validation failures.

## Technical Details

The API support already exists through `CreateShareFileRequest(Guid FileId, String? DisplayName = null)`, `CreateShareService.NormalizeDisplayName`, and `DownloadFileService` using `fileEntry.DisplayName ?? fileEntry.OriginalFileName`. This plan should therefore stay mostly in the CLI command model, option parsing, request construction, and output verification paths.

Use a small, script-friendly option contract rather than an interactive prompt. A likely shape is:

- `upload <path> --name <display-name>` for the common single-file end-to-end case.
- Repeated explicit mappings for multi-file and lower-level cases, such as `--display-name <path-or-file-id>=<display-name>`, where the left side must match exactly one uploaded path or file id in the current command.

The implementation should reject ambiguous or malformed mappings before creating the share. Validation should cover duplicate mappings, mappings that do not correspond to any selected file, display names that normalize to empty, and single-file-only options used with multiple inputs. Display names should be trimmed through the same normalization behavior as the API. Since `CreateShareService.NormalizeDisplayName` is currently `private` and runs server-side, promote it into a `public` helper in `ShadowDrop.Shared` (referenced by both the API and the CLI), so the CLI resolves locally generated artifacts (queue files, curl `-o`, manifest, download destination) to the same value the server would produce.

For `upload`, carry the resolved display names from parsed options into the `CreateShareFileRequest` entries built after file upload. Note that `UploadCommandHandler` currently hardcodes the original file name as the display name when constructing each `CreateShareCliFileRequest`; replace that with the resolved override and send `null` when no override is given, so the API-side fallback to the original file name behaves as intended. For `share create`, apply mappings directly to the supplied file ids when constructing the create-share request. Keep the internal representation keyed by a stable identifier so text output, JSON output, queue creation, and direct HTTP command generation all observe the same resolved display name.

Review the output paths that currently use original file names. The generated share manifest, queue files, direct HTTP `curl-command -o` target, and CLI download destination selection should use the display name when present and continue to fall back to the original file name otherwise. Existing `share-url` behavior should remain compatible, with only recipient-facing names changing where the display name is part of downloaded or generated artifacts.

Automated tests should include both command parsing / validation coverage and workflow-level assertions. Cover at least the single-file `upload --name` case, multi-file explicit mapping, `share create` display-name mapping by file id, queue output file names, direct HTTP curl `-o` names, and representative validation failures for ambiguous, unknown, duplicate, and empty-normalized names.
