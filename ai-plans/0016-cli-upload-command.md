## Rationale

Add the first end-user CLI workflow so senders can upload files with client-side encryption from scripts and terminals.
This slice should prove the CLI can read local files, encrypt them chunk-by-chunk, and call the protected upload API
non-interactively.

## Acceptance Criteria

- [ ] A non-interactive `shadowdrop upload` command exists.
- [ ] The command accepts one or more local file paths.
- [ ] The command resolves server URL and upload authorization token from flags, environment variables, or config with
  explicit precedence: flags > environment variables > config file.
- [ ] The command encrypts files client-side using the shared chunked AES-256-GCM implementation.
- [ ] The command streams encrypted content to the upload API using bounded buffers, never accumulating full plaintext
  in memory during encryption or upload.
- [ ] The command sends only encrypted content and non-secret metadata to the API.
- [ ] The command outputs successful file ids to stdout in script-friendly format (one per line); all diagnostic output
  and errors go to stderr.
- [ ] The command exits with code 0 on complete success; non-zero on any failure (file read error, validation error, API
  error, or partial upload failure).
- [ ] The command produces clear validation errors for missing files, unreadable files, missing server URL, and missing
  upload authorization token; error messages are generic and do not expose file paths, server URLs, tokens, or internal
  server details.
- [ ] The command remains compatible with Native AOT.
- [ ] Automated tests cover command parsing, configuration precedence, and upload workflow behavior against a test
  server.
- [ ] Upload authorization token is trimmed before use; plaintext token is never logged, persisted, or exposed in
  command output or telemetry. CLI flags may expose tokens to process inspection; users should prefer environment
  variables or config file for sensitive deployments.

## Technical Details

Implement the command in `ShadowDrop.Cli` with System.CommandLine. Keep the command non-interactive and
automation-friendly: explicit arguments and flags first, with config and environment variable fallback. Spectre.Console
may be used for terminal rendering, but this slice should not depend on wizard-style prompts.

### Configuration Precedence & Token Handling

Config resolution follows strict precedence: **flags > environment variables > config file**. This order is binding and
non-negotiable; any deviation creates footgun scenarios where users cannot override stale config values.

- **Upload authorization token:** Trim whitespace before use (matching server-side normalization). **Never log, cache,
  or persist plaintext token in stdout/stderr, debug output, or telemetry.** If users pass token via CLI flag (e.g.,
  `--upload-token $USERTOKEN`), the token may be visible to process inspection tools (e.g., `ps` on Unix, Process
  Explorer on Windows). Document this visibility risk and recommend users prefer environment variables or config file
  for sensitive deployments.

### CLI Output Contract

Establish a strict contract to make output script-friendly and debuggable:

- **stdout:** Successful file ids only, one per line. No diagnostic messages or progress output.
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
  Implementer may implement exponential backoff with bounded retry count (e.g., up to 3 retries with jitter).
- **Partial failure:** If one file succeeds and another fails, the command exits non-zero. Failed files are not retried
  automatically; user must re-run the command with the failed file paths.
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
- **Emission:** The CLI may emit the plaintext secret to stdout (if `--output-secret` flag or equivalent is provided) or
  stderr (diagnostic mode), but **never by default**. The user must explicitly opt-in to receive the secret. If emitted,
  it is the caller's responsibility to capture, persist, and pass it to downstream share-creation or download commands.
- **Server-side:** The CLI does not send the plaintext secret to the server. Only the encrypted file content and
  non-secret metadata (plaintext file length, encrypted length, chunk count, etc.) are uploaded. The server does not
  know or store the share secret.
- **Downstream share creation:** After upload, a separate share-creation command (outside this slice) will accept the
  share secret as input along with uploaded file ids. That command creates the actual share record and tokens.
- **Best practice:** Users are expected to capture and securely transmit the secret from the upload command's output to
  the share-creation endpoint, or manage it out-of-band (e.g., read from a secure key store, HSM, or secrets manager).

Rationale: Keeps the upload slice narrow (intake-only), avoids server-side secret storage, and enforces zero-knowledge
architecture where the server never knows the share secret.

### Testing & AOT Compatibility

Design the HTTP client path and serializer choices with Native AOT in mind. Prefer explicit JSON contracts and
source-generation-friendly patterns over reflection-heavy convenience APIs. Tests can use an in-process API host or
equivalent test server to verify:

- Command parsing and config precedence behavior (all three sources tested).
- Token trimming and non-logging invariant (grep test output for token value; assert absence).
- Encryption and streaming with multiple file sizes, including empty files and large files requiring multiple chunks.
- CLI output contract (verify stdout contains only file ids, stderr contains diagnostics, exit codes match outcomes).
- Error scenarios (missing file, API failure, partial upload) produce generic errors with no secret/path exposure.
- Native AOT publish succeeds for `ShadowDrop.Cli` without reflection surprises.
