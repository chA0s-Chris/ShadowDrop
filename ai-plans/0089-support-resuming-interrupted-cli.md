# Support resuming interrupted CLI downloads

> Issue: [#89](https://github.com/chA0s-Chris/ShadowDrop/issues/89)

## Rationale

Queue downloads currently discard useful work after cancellation or transient failure: `DownloadToFileAsync` recreates the
partial file on each attempt and deletes it when any exception is thrown. The lower-level `CliDownloadSession` already
has range-aware resume support, so the queue/file orchestration layer should preserve durable plaintext bytes and seed
the session with the existing partial length.

This plan scopes resume support to queue and interactive file downloads that write to seekable files. Direct downloads
to stdout remain non-resumable because stdout is not a durable, seekable destination.

## Acceptance Criteria

- [x] Cancelling a queue download and rerunning the same queue continues from the existing `.shadowdrop-partial` bytes instead of restarting from byte zero.
- [x] A failed queue file download keeps its `.shadowdrop-partial` file when the resume marker matches the server URL, share token, file id, file name, file length, KDF salt/hash, and plaintext SHA-256 when available.
- [x] A partial file that does not match the current queue entry and share manifest is discarded and redownloaded from byte zero.
- [x] Resume happens automatically when a valid partial exists; no new `--resume` flag is required.
- [x] Re-running a queue skips an existing completed output file when it matches the queue entry length and, when available, the queue entry plaintext SHA-256.
- [x] Re-running a queue resumes the first incomplete output from a valid `.shadowdrop-partial` file and then continues with the remaining queue entries.
- [x] An existing completed output file whose length or plaintext SHA-256 does not match the queue entry fails that queue entry with a clear error and leaves the existing file untouched, rather than being silently overwritten.
- [x] Interactive file downloads reuse the same validated partial-file resume behavior as queue downloads.
- [x] Progress output starts from the already durable byte count when a file is resumed.
- [x] Completed resumed downloads are byte-for-byte identical to clean, uninterrupted downloads.
- [x] Direct single-file downloads to stdout remain unchanged and do not attempt resume.
- [x] Automated tests cover cancellation/failure partial preservation, successful resume from a matching partial, stale partial cleanup, progress starting offset, final byte equality, and unchanged stdout download behavior.

## Technical Details

Extend `DownloadCommandHandler.DownloadToFileAsync` so it opens `{outputPath}.shadowdrop-partial` with
`FileMode.OpenOrCreate` and computes `durablePlaintextLength` from the file's current length before starting the
download. The stream must be seekable and positioned at the durable length before passing it into the session.

Add a durable resume marker next to the partial file, for example `{outputPath}.shadowdrop-partial.json`, that stores
only queue/share metadata needed to validate reuse: marker version, server URL, share token, file id, file name, file
length, KDF salt or its hash, and the plaintext SHA-256 when the queue entry or manifest provides it. Do not store share
keys, bearer tokens, or decrypted content. Before resuming, compare
the marker, queue entry, and current manifest-selected `ShareManifestFileContract`; if any required field is missing or
does not match, delete the stale partial and marker and restart from byte zero.

Split the file-download orchestration into small helpers so the policy is testable without full CLI execution:
resolve the partial and marker paths, load/validate the marker, reset stale resume state, open the partial stream, and
persist the marker before issuing the download request. Keep the final move atomic: after the session completes and the
stream is flushed, move the partial file to the final output path and delete the marker.

Before doing any network work, check whether a completed output already exists at the final path. Compare its on-disk
length against the queue entry length and, when the queue entry or manifest carries a plaintext SHA-256, hash the
existing file and compare it. If the existing output matches, skip the entry as already downloaded. If it exists but does
not match, fail that queue entry with a clear error and leave the existing file untouched rather than overwriting it.
Only entries that are actually downloaded or resumed reach the final `File.Move(partialPath, outputPath, true)`, which
overwrites the partial's own prior output; this skip-and-guard check always runs first and takes precedence over the
unconditional move.

Extend `DownloadToStreamAsync` or add a file-specific overload so `CliDownloadSession` receives the computed
`durablePlaintextLength` and, for resumed downloads, knows the total plaintext size from the manifest length before it
creates the range request. `CliDownloadSession.CreateExpectedRange` currently requires `TotalPlaintextSize` for
non-zero starts, so the orchestration layer must provide that value either through a constructor parameter or a small
session method before `DownloadAsync`.

Exception handling should preserve partial state for cancellation, server errors, local I/O failures after partial bytes
were written, and decryption failures unless the validation step already determined the state is stale. A failed resumed
download should leave the newer durable length in place for the next retry. If the final move succeeds, remove the
marker; if marker cleanup fails after the final file is already in place, treat that as local I/O failure reporting but
do not delete the completed output.

Progress reporting already receives cumulative durable plaintext byte counts from `CliDownloadSession`. Preserve that
contract by reporting the starting durable length when a resumed session is created. Per-file progress percentages and
success lines should treat the resumed starting offset as already completed file bytes. Queue-level throughput may count
only bytes transferred during the current invocation if the reporter separates that accounting from per-file progress.
Verify both plain-text and rich reporters tolerate an initial non-zero progress value.

Update `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs` with focused handler tests using a fake
HTTP response stream that can be cancelled after some plaintext bytes are written, then rerun with the same partial
state and assert the outgoing request uses the expected range. Add stale-marker tests that reuse the same output path
with a different file id, file length, file name, or KDF salt and assert the old partial is deleted before the new
download. Add at least one end-to-end queue-style test that compares the final file bytes against the original plaintext
after an interrupted/resumed run.
