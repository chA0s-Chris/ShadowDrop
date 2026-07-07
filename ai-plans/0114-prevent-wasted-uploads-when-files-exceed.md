## Rationale

Issue [#114](https://github.com/chA0s-Chris/ShadowDrop/issues/114) prevents the CLI from wasting bandwidth and server I/O when selected files exceed the configured upload size limit. Today, an oversized upload streams data until the server-side `Upload:MaxBytes` limit is reached and only then fails. In a multi-file upload, the CLI then continues to the next file, so a batch of consistently oversized files can transfer many gigabytes of useless data before the command reports failure.

The upload workflow should validate the full selected batch before sending file contents. Client-side validation is an efficiency and UX improvement only; the server remains authoritative and must continue enforcing the configured upload limit independently.

## Acceptance Criteria

- [ ] The API provides a way for the CLI to know the effective maximum file payload size before file contents are sent.
- [ ] If the CLI cannot resolve the effective upload limit from the server, the upload command fails with a clear error and a non-zero exit code before any upload starts.
- [ ] The CLI preflights all selected files before uploading the first byte.
- [ ] If any selected file exceeds the maximum upload size, the CLI uploads zero bytes for the entire batch and exits with failure.
- [ ] The CLI reports which file or files exceeded the limit and includes the configured maximum size in the error output.
- [ ] If any upload fails at runtime, the CLI stops the remaining batch and exits with failure.
- [ ] The server still enforces the upload size limit independently of the CLI.
- [ ] Automated tests cover oversized single-file upload and oversized multi-file upload behavior.

## Technical Details

Add a small authenticated API surface that exposes upload limits to the CLI before content upload starts. Prefer an admin upload capabilities/config endpoint, or extend an existing upload reservation/init flow if that creates a cleaner contract. The configured `Upload:MaxBytes` limit applies to the whole multipart request body, so the response must advertise an effective maximum file payload size in bytes: the server derives it from `Upload:MaxBytes` minus a conservative, documented allowance for the multipart envelope (metadata section and boundaries). This keeps all envelope accounting on the server; the CLI never estimates multipart overhead. Keep the server-side `Upload:MaxBytes` enforcement in the multipart reader path so malicious or stale clients cannot bypass the limit.

Update the end-to-end upload command and the raw upload command so they resolve the effective upload limit before encrypting or uploading file contents. Resolving the limit is a required step: if the CLI cannot obtain it — because the server does not expose the contract or the call fails — the command fails with a clear error and a non-zero exit code before any upload starts; there is no preflight-less fallback. Preflight all selected local files as one batch using local file metadata. Account for the encrypted upload size rather than only plaintext file size: the CLI already knows the chunk size and encryption format overhead it will produce, so compare the encrypted payload size directly against the advertised maximum file payload size. If any file is too large or otherwise invalid during this preflight, report all known invalid files and exit before starting any upload.

Change multi-file upload execution to fail fast after transfer begins. Once preflight has succeeded, files may still fail at runtime because of network, server, storage, or validation errors. When that happens, stop processing the remaining files, preserve a clear failure message, and return a non-zero exit code. Do not add best-effort or partial-upload behavior as part of this plan.

Keep output contracts in mind when reporting errors. Oversized-file errors should go to stderr for normal text output and should include enough context for users to fix the batch: file path or display name, computed upload size, and maximum size. If JSON output is supported for the affected command path, represent the same failure clearly without emitting success records for files that were never attempted.

Add focused tests around the new limit contract and CLI behavior. Cover a single oversized file, a multi-file batch where one file is oversized, a multi-file batch where several files are oversized, and a runtime failure after one file has started uploading to ensure later files are not attempted. Include API tests for the limit-discovery endpoint or upload-init contract and keep existing server-side oversized request rejection tests in place.
