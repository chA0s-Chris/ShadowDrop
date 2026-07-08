## Rationale

Issue [#117](https://github.com/chA0s-Chris/ShadowDrop/issues/117) improves API observability for
operators. The API currently logs only limited startup and runtime information, which makes it harder
to verify effective configuration, inspect metadata/storage state, and diagnose failures in upload,
download, share, cleanup, and blob-storage workflows.

Add structured logs for startup configuration, startup state summaries, important lifecycle events,
and exception-bearing failures. The logging must preserve existing HTTP behavior and must not expose
secrets, tokens, keys, plaintext file contents, or cryptographic material.

## Acceptance Criteria

- [x] API startup logs include an effective configuration summary without secrets.
- [x] API startup logs include metadata-derived counts and stored blob bytes without recursively
  scanning blob storage.
- [x] Startup summary stats are exposed through dedicated repository/service methods; the host layer
  does not query LiteDB or blob storage directly.
- [x] Upload reservation lifecycle events (creation, claim, completion, expiration, release) emit
  structured logs with non-secret identifiers and levels per the level policy in the technical
  details.
- [x] Upload completion emits structured logs with non-secret identifiers and sizes.
- [x] Blob storage save/open/delete operations emit structured logs at the levels defined by the
  level policy.
- [x] Share creation and revocation emit structured logs with non-secret share context.
- [x] Cleanup runs emit structured logs distinguishing skipped, started, completed, and failed runs.
- [x] Download manifest, file, and range resolution failures emit structured logs with safe
  share/file context.
- [x] Unexpected exceptions in storage/metadata/download/upload paths are logged with exception
  details and non-secret context.
- [x] Expected validation/client errors are logged at warning level or below, per the level policy
  in the technical details.
- [x] Logs never include admin tokens, share tokens, download bearer tokens, direct-download keys,
  share keys, KDF material, or plaintext file contents.
- [x] Repository stats methods have automated tests for completed file count, encrypted byte totals,
  share status counts, and active pending reservation count.
- [x] Key logging paths have automated tests asserting log event presence, level, and absence of
  secret values in structured state.

## Technical Details

Add API startup logging after configuration binding and service initialization have enough information
to report effective settings. Include the blob storage root path, LiteDB metadata database path,
upload `MaxBytes`, effective Kestrel max request body size when derived from upload settings, API
exposure flags for public downloads and admin operations, and cleanup schedule configuration. Keep
these messages concise and structured so deployments can filter on fields instead of parsing text.

Expose cheap metadata-derived startup stats through repository or service methods rather than
querying storage directly from the host layer. Completed uploaded-file counts and stored blob bytes
should come from persisted uploaded-file metadata, with byte totals summed from `EncryptedLength`.
Share counts should be split into active, expired, revoked, cleanup-completed, and cleanup-failed
states, derived from `ExpiresAtUtc`, `RevokedAtUtc`, and `ShareCleanupState`. Active pending upload reservation count is in scope and should
count unclaimed, unexpired reservations from metadata. Avoid recursive blob-storage scans in startup
code.

Add structured lifecycle logs at the service boundaries that already own each operation. Upload
reservation logs should cover creation, claim, completion, expiration, and release. Upload completion
logs should include non-secret identifiers and sizes such as file id, blob key, plaintext length,
encrypted length, chunk size/count, and content type when available. Blob save/open/delete logs can
generally be debug level unless they represent unusual conditions. Share logs should cover creation,
revocation, cleanup runs/results, file count, expiration, direct-HTTP status, and whether a bearer
token was generated without logging token values. Cleanup logs should distinguish skipped runs,
started runs, completed runs, and failures. Download logs should cover manifest, file, and range
resolution failures with safe share/file context. Use information level for successful operator-facing
lifecycle events, debug level for high-volume blob and range details, warning level for expected
validation/client failures, and error level for unexpected exceptions.

Improve failure logging around storage, metadata, crypto, I/O, download resolution, and upload paths.
Unexpected exceptions should be logged with the exception object and useful non-secret context such as
file id, share id, blob key, operation name, and safe request context. Expected validation and client
mistakes should remain warning or information level as appropriate to avoid noisy error logs, and all
changes should preserve the current response contracts.

Review every new log message for secret safety. Never log admin tokens, share tokens, download bearer
tokens, direct-download keys, share keys, KDF inputs or outputs, plaintext content, or other
cryptographic material. Add focused automated coverage for stats calculations or repository stats
methods where those methods are introduced or changed. Verify log emission on key paths (upload
reservation lifecycle, upload completion, share creation/revocation, cleanup runs, download
resolution failures) with `FakeLogger` from `Microsoft.Extensions.Diagnostics.Testing`: assert event
presence, log level, and structured state — including that no secret-bearing keys or values appear —
rather than matching message text. Verify the startup configuration summary manually once by running
the API and reviewing the emitted summary.
