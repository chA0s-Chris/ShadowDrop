## Rationale

Add the first end-user CLI workflow so senders can upload files with client-side encryption from scripts and terminals.
This slice should prove the CLI can read local files, encrypt them chunk-by-chunk, and call the protected upload API
non-interactively.

## Acceptance Criteria

- [x] A non-interactive `shadowdrop upload` command exists.
- [x] The command accepts one or more local file paths.
- [x] The command resolves server URL and upload authorization token from flags, environment variables, or config with
  explicit precedence: flags > environment variables > config file.
- [x] The command encrypts files client-side using the shared chunked AES-256-GCM implementation.
- [x] The command streams encrypted content to the upload API using bounded buffers, never accumulating full plaintext
  in memory during encryption or upload.
- [x] The command sends only encrypted content and non-secret metadata to the API.
- [x] The command outputs successful file ids to stdout in argument order, one per line; all diagnostic output and
  errors
  go to stderr. In multi-file uploads where some files succeed and some fail, ids for completed files still appear on
  stdout and the command exits non-zero; callers must always check exit code to distinguish partial success from full
  success.
- [x] When `--output-secret` is passed, the command emits the plaintext share secret to stdout as a final line in the
  format `secret:<hex-encoded-value>` after all file id lines, but only on full success (exit 0). On any failure, no
  secret is emitted. The secret is never emitted by default.
- [x] The command exits with code 0 on complete success; non-zero on any failure (file read error, validation error, API
  error, or partial upload failure).
- [x] The command produces clear validation errors for missing files, unreadable files, missing server URL, and missing
  upload authorization token; error messages are generic and do not expose file paths, server URLs, tokens, or internal
  server details.
- [x] The command remains compatible with Native AOT.
- [x] Automated tests cover command parsing, configuration precedence, and upload workflow behavior against a test
  server.
- [x] Upload authorization token is trimmed before use; plaintext token is never logged, persisted, or exposed in
  command output or telemetry. CLI flags may expose tokens to process inspection; users should prefer environment
  variables or config file for sensitive deployments.
- [x] Automated tests assert the upload authorization token never appears in captured stdout, stderr, or log output
  (token non-leakage is a hard tested invariant, not just convention).

## Technical Details

Implement the command in `ShadowDrop.Cli` with System.CommandLine. Keep the command non-interactive and
automation-friendly: explicit arguments and flags first, with config and environment variable fallback. Spectre.Console
may be used for terminal rendering, but this slice should not depend on wizard-style prompts.

### Configuration Precedence & Token Handling

Config resolution follows strict precedence: **flags > environment variables > config file**. This order is binding and
non-negotiable; any deviation creates footgun scenarios where users cannot override stale config values.

Environment variable names: `SHADOWDROP_SERVER_URL` (server URL) and `SHADOWDROP_UPLOAD_TOKEN` (upload authorization
token). These names are binding; implementations must not use alternate names or aliases.

- **Upload authorization token:** Trim whitespace before use (matching server-side normalization). **Never log, cache,
  or persist plaintext token in stdout/stderr, debug output, or telemetry.** If users pass token via CLI flag (e.g.,
  `--upload-token $USERTOKEN`), the token may be visible to process inspection tools (e.g., `ps` on Unix, Process
  Explorer on Windows, or `/proc/*/cmdline` inspection). This is a fundamental limit of CLI argument passing that the
  CLI itself cannot eliminate. **Users are responsible for choosing the input method**; prefer `SHADOWDROP_UPLOAD_TOKEN`
  environment variable or config file for sensitive deployments. Document this visibility risk explicitly in user-facing
  help text and CLI usage docs.

### CLI Output Contract

Establish a strict contract to make output script-friendly and debuggable:

- **stdout:** Successful file ids only, one per line, in argument order. File ids are written per-file as each upload
  completes; callers must check exit code to know whether all files succeeded. When `--output-secret` is passed and all
  files succeeded (exit 0), the secret line (`secret:<hex-encoded-value>`) is the final stdout line. On any failure,
  no secret line is emitted. No diagnostic messages or progress output on this channel.
- **stderr:** All diagnostic messages, progress, validation errors, and warnings. Use this channel for human
  readability.
- **Exit code:** Return 0 if and only if all files uploaded successfully. Return non-zero on any failure: missing file,
  unreadable file, validation error, API error, network failure, or partial upload failure. Failures are all-or-nothing
  at the command level; partial success is not reported as exit 0.

### Error Messages & Security

Error messages must be **clear but generic**:

- **Never expose:** file paths, server URLs, token values, encryption key material, internal server details, or stack
  traces.
- **Examples of unsafe:** "Failed to connect to https://api.internal.corp:9443" (exposes URL), "Token not found in
  SHADOWDROP_ADMIN_TOKEN" (exposes secret name), "Key derivation failed: salt mismatch" (exposes algorithm detail).
- **Examples of safe:** "Server connection failed", "Authentication token invalid or missing", "Upload failed; please
  verify file and try again".

### HTTP Status & Retry Behavior

Establish clear retry semantics so implementers know which failures are transient vs. permanent:

- **Immediate failure (no retry):** HTTP 401 (unauthorized token), 403 (permission denied), 400 (malformed request,
  missing required fields, file unreadable), 413 (payload too large). These indicate client-side issues that retry
  cannot fix.
- **Transient/retriable:** HTTP 429 (rate limited), 503 (server unavailable), network timeouts, and connection errors.
  Implement exponential backoff with a bounded retry count. The current CLI contract uses up to 3 total attempts with
  exponential delays between retries.
- **Partial failure:** If one file succeeds and another fails, the command exits non-zero and reports which files
  failed on stderr. The CLI does not automatically retry failed files or track upload state between invocations. The
  caller must re-run the command with only the failed file paths. If a file is successfully uploaded and then
  re-uploaded, the server assigns a new file id each time; deduplication is not performed in this slice.
- **All-or-nothing per file:** Each file upload is atomic. If a single file's upload stream is interrupted mid-transfer,
  that file's upload fails and is rolled back server-side (matching upload API all-or-nothing semantics). The CLI does
  not attempt to resume partial uploads.

Rationale: Distinguishes between permanent client errors (no point retrying) and transient network/server issues (retry
is appropriate). Prevents retry storms on authentication failures.

### Encryption & Streaming

- Use `ShadowDrop.Shared` for the encryption format and shared contracts. Generate the share-level encryption secret
  client-side and derive per-file keys as needed.
- **Stream encrypted content with bounded buffers.** Do not accumulate full plaintext in memory during encryption or
  upload. Use fixed-size chunk buffers (e.g., 1 MB) to read plaintext, encrypt chunk-by-chunk, and write encrypted bytes
  directly to the HTTP stream. Keep total plaintext buffer size bounded and deallocate between chunks.
- Upload encrypted bytes plus non-secret metadata only. **This command performs upload intake only and does not create
  shares or attach files to existing shares**; it stops after file intake and reports the resulting file ids.

### Share-Level Secret Lifecycle

The CLI **generates the share-level encryption secret client-side and is responsible for its lifecycle**. Implementers
and downstream callers must understand the following:

- **Secret generation & ownership:** The CLI generates a cryptographically random share-level secret (256 bits) at the
  start of the upload command. This secret is the **caller's responsibility to manage**; the CLI does not persist it,
  store it in a config file, or cache it.
- **Emission:** When `--output-secret` is passed, the CLI **must** emit the plaintext share secret to stdout as a
  final line formatted `secret:<hex-encoded-value>` after all file id lines, **only on full success (exit 0)**. On any
  failure, the secret is not emitted — callers cannot reconstruct it and must re-run. The secret is **never emitted
  by default**. If emitted, it is the caller's responsibility to capture, persist, and pass it to downstream
  share-creation or download commands. The secret must never appear in stderr, logs, or telemetry regardless of the
  `--output-secret` flag.
- **Server-side:** The CLI does not send the plaintext secret to the server. Only the encrypted file content and
  non-secret metadata (plaintext file length, encrypted length, chunk count, etc.) are uploaded. The server does not
  know or store the share secret.
- **Downstream share creation:** After upload, a separate share-creation command (outside this slice) will accept the
  share secret as input along with uploaded file ids. That command creates the actual share record and tokens.
- **Best practice:** Users are expected to capture and securely transmit the secret from the upload command's output to
  the share-creation endpoint, or manage it out-of-band (e.g., read from a secure key store, HSM, or secrets manager).

Rationale: Keeps the upload slice narrow (intake-only), avoids server-side secret storage, and enforces zero-knowledge
architecture where the server never knows the share secret.

### Empty File Handling

**Empty files (zero-byte size) are intentionally rejected.** The upload command rejects any file with zero bytes before
attempting upload and returns a validation error with exit code non-zero. Rationale: empty files have no plaintext to
encrypt and no cryptographic meaning in the chunked AES-256-GCM contract. Clients must pre-filter empty files or handle
the validation error. This is a permanent design decision, not a future enhancement.

### Testing & AOT Compatibility

Design the HTTP client path and serializer choices with Native AOT in mind. Prefer explicit JSON contracts and
source-generation-friendly patterns over reflection-heavy convenience APIs. Tests can use an in-process API host or
equivalent test server to verify:

- Command parsing and config precedence behavior (all three sources tested).
- Token trimming and non-leakage invariant: tests **must** assert the upload authorization token string never appears
  in captured stdout, stderr, or log output — this is a hard assertion, not convention. Grep captured output for the
  token value and fail the test if found.
- Secret non-leakage invariant: when `--output-secret` is NOT passed, assert the share secret never appears on stdout
  or stderr.
- Empty file validation: attempting to upload zero-byte files is rejected with non-zero exit and a generic validation
  error.
- Encryption and streaming with multiple file sizes (> 0 bytes) and large files requiring multiple chunks.
- CLI output contract (verify stdout contains only file ids, stderr contains diagnostics, exit codes match outcomes).
- Error scenarios (missing file, unreadable file, empty file, API failure, partial upload) produce generic errors with
  no secret/path exposure.
- Native AOT publish succeeds for `ShadowDrop.Cli` without reflection surprises.
