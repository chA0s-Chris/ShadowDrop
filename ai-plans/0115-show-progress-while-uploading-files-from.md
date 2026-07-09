## Rationale

Issue [#115](https://github.com/chA0s-Chris/ShadowDrop/issues/115) asks for live CLI feedback while upload bytes are being transferred. Today the upload path validates files, encrypts and streams them to the API, and only prints per-file output after an upload has completed or failed. Large files and multi-file batches can therefore look stalled even when data is actively moving.

The upload commands should get progress behavior that feels consistent with the existing download progress experience while preserving the upload commands' script-friendly stdout contracts. Progress, lifecycle, and failure diagnostics should stay off stdout for JSON and key-value output modes, and final success or failure output should remain clear after any transient progress UI completes.

Out of scope: the existing download progress stack stays unchanged apart from extracting small neutral shared helpers, and there is no cumulative cross-retry byte accounting — a retried attempt resets per-file progress as described in the technical details.

## Acceptance Criteria

- [x] Single-file CLI uploads display progress while bytes are being transferred.
- [x] Multi-file CLI uploads display progress while each file is being transferred.
- [x] Multi-file progress includes the current file position and total file count.
- [x] Progress reporting applies to both `shadowdrop upload` and `shadowdrop upload raw`.
- [x] Upload progress uses the same terminal mode selection, human-readable size and speed formatting, and rich/plain output conventions as download progress.
- [x] Upload progress includes transferred bytes, total bytes, and percentage; transfer speed is shown once enough elapsed time has passed to compute it.
- [x] Final success output on stdout (`share-url:`, `share-key:`, `file-id:`, or JSON) is byte-identical to current output, and any progress UI is finished or cleared before it is written.
- [x] On failure, the final error output on stderr is written after the progress UI is finished or cleared and identifies the failed file.
- [x] Non-interactive, redirected, and CI runs use deterministic plain progress output on stderr instead of interactive rendering.
- [x] The default (key-value) output mode displays upload progress on stderr; the `--json` output mode emits no progress or lifecycle output at all.
- [x] Progress and lifecycle output is never written to stdout in any mode.
- [x] Tests cover encrypted content progress reporting, single-file upload progress, multi-file upload progress, failure output, JSON stdout preservation, and upload progress mode selection.

## Technical Details

Introduce an upload progress reporting abstraction rather than extending the current `UploadProgressReporter` post-result helper in place. The shape can mirror the download progress stack in `ShadowDrop.Cli.Downloads.Progress`: a mode selector, a rich Spectre.Console reporter for interactive terminals, a deterministic plain reporter for redirected/CI runs, and a null reporter for `--json`, which emits no progress or lifecycle output at all, only final output and real errors. Because upload progress renders on stderr (not stdout as downloads do), the mode selection follows stderr's capabilities via `ITerminalCapabilityProvider.DetectForStandardError()`. Unlike downloads, upload command stdout is always a parseable contract (`share-url:`, `share-key:`, `file-id:`, or JSON), so upload progress and lifecycle lines always go to stderr, never stdout, and are suppressed entirely under `--json`.

Thread progress through the upload execution path. `UploadCommandHandler` and `UploadRawCommandHandler` should create the reporter and pass it into `UploadCommandExecutor.ExecuteAsync`. The executor already owns batch order, file numbers, preflight, and failure stopping behavior, so it is the right layer to call file-start, file-success, file-failure, and batch-complete reporter methods. Keep the existing final result handling in the command handlers so share creation, queue generation, credential delivery, and JSON/key-value output are not mixed into the progress implementation. `upload --interactive` remains in scope only after the guided prompts complete: once it delegates to `UploadCommandHandler`, it should use the same upload progress behavior as the non-interactive `upload` command.

Add an optional `IProgress<long>` sink to the upload byte stream. `UploadApiClient.UploadAsync`, `CreateMultipartContent`, and `EncryptedFileContent` should accept a per-file progress sink and report cumulative bytes from `EncryptedFileContent.SerializeToStreamAsync` after each encrypted chunk is written to the request stream. Use the encrypted bytes written as the transferred count because that is the actual HTTP request payload being sent, and use the precomputed encrypted length from `UploadFilePlan.Metadata.EncryptedLength` as the total. The reporter can display the original filename from the plan or file info, the current file index, total file count, current bytes, total bytes, percentage, elapsed time, and average transfer speed.

Reuse existing download helpers where doing so keeps behavior consistent without coupling upload reporting to download-specific names. `HumanReadableSize`, `TrackingProgress`, terminal-capability detection, Spectre progress columns, and deterministic plain-text line conventions are all useful patterns. If shared code is needed, extract small neutral helpers instead of making upload code depend on download-only types. Plain non-interactive output should be sparse and deterministic, for example start/success/failure lines, while rich interactive output can render a live progress bar with file and batch context.

Handle failure and retry semantics carefully. `UploadApiClient.SendWithRetryAsync` can recreate request content for transient retries, so progress may restart for an attempt. The reporter should avoid presenting retried bytes as a single monotonic upload unless the implementation explicitly models attempts. When a retry restarts request streaming, reset the per-file progress task and emit a single deterministic retry line before the restarted attempt; still report only one terminal success or failure for the file. Cancellation and exceptions should leave the progress UI in a completed state before the command writes final failure output.

Update tests around the upload commands and lower-level content streaming. Add focused tests for `EncryptedFileContent` progress reporting, upload executor reporter calls for single-file and multi-file success/failure paths, JSON mode suppressing noisy interactive output, stdout remaining parseable, and stderr carrying progress/failure information. Prefer injectable reporters or fixed terminal capabilities so tests do not depend on the real terminal, wall-clock timing, or live network behavior.
