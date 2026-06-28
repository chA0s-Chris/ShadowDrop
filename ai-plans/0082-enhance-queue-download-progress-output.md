# Enhance download progress output

> Issue: [#82](https://github.com/chA0s-Chris/ShadowDrop/issues/82)

## Rationale

Queue downloads currently emit one plain stderr line after each file finishes, and non-interactive single-file downloads stream the downloaded bytes to stdout without progress feedback. That is enough for scripting, but it does not give a human running `shadowdrop download ...` useful feedback about the active file, progress, speed, remaining time, or final throughput.

The goal is to add consistent download progress output for both non-interactive single-file and queue downloads while keeping all progress off stdout, preserving stdout as the single-file byte stream and preserving existing queue success/failure semantics. The CLI should introduce two stderr output modes: a rich Spectre.Console experience for interactive terminals and deterministic plain text for redirected stderr, CI, and other non-interactive environments. The guided `download --interactive` workflow is out of scope and should keep its existing UX.

## Acceptance Criteria

- [x] Queue downloads report the current file index and total file count.
- [x] Non-interactive single-file downloads report the current file name and human-readable decimal file size.
- [x] Non-interactive single-file downloads report live progress percentage in interactive terminal mode while the file is downloading.
- [x] Non-interactive single-file downloads report current download speed in interactive terminal mode.
- [x] Non-interactive single-file downloads estimate remaining download time in interactive terminal mode.
- [x] Queue downloads report the current file name and human-readable decimal file size.
- [x] Queue downloads report live per-file progress percentage in interactive mode while the active file is downloading.
- [x] Queue downloads report current download speed in interactive mode.
- [x] Queue downloads estimate remaining time for the current file in interactive mode.
- [x] Queue downloads estimate remaining time for the entire queue in interactive mode, derived from the summed file lengths declared in the queue file.
- [x] Interactive stderr uses Spectre.Console rich progress/status output, including an active-file spinner where supported.
- [x] Redirected stderr, CI, or non-interactive terminals use deterministic plain text output.
- [x] Non-interactive single-file completion output includes total downloaded bytes with human-readable decimal units, elapsed time, and average speed.
- [x] Queue final summary includes downloaded file count, failed file count, total downloaded bytes with human-readable decimal units, elapsed time, and average speed.
- [x] Failed files are represented in progress output and the final summary without stopping the rest of the queue.
- [x] Existing queue success/failure exit-code semantics are preserved: exit `0` only when every queued file succeeds, otherwise exit `1`.
- [x] All progress and summary output is written to stderr only and does not write progress or summary text to stdout.
- [x] Automated tests verify output behavior without relying on terminal capabilities by injecting the progress reporter (or forcing plain mode) and asserting the emitted plain-text lines, plus a focused test for reporter mode selection.

## Technical Details

Introduce a shared download progress reporting abstraction that can be used by both the direct non-interactive single-file path in `DownloadCommandHandler.ExecuteAsync` and the queue path in `DownloadCommandHandler.ExecuteQueueAsync`. The abstraction should expose lifecycle methods for single-download start, progress, success, and failure, plus queue-specific lifecycle methods for queue start, file start, file progress, file success, file failure, and queue completion. The existing per-file queue `SUCCESS ...` and `FAILED ...` stderr output should move behind this abstraction instead of staying as direct `standardError.WriteLineAsync` calls.

Extend the non-interactive single-file path so it reports the selected file name, size, live percentage, speed, ETA, and completion summary while continuing to stream decrypted bytes only to `standardOutStream`. Extend the queue path so it tracks total file count, one-based file position, aggregate downloaded bytes, failures, elapsed time, and per-file progress. Do not change `InteractiveDownloadCommandHandler` or the guided `download --interactive` UX as part of this work; even though it calls `DownloadToFileAsync`, it should continue to own its current prompts and completion summary.

Source per-file size and the whole-queue ETA from `QueueFileEntry.Length`, which the queue file already declares for each entry (populated by `QueueFileBuilder` from the share manifest). Sum the entries' `Length` to get the total queue bytes and compute a bytes-based whole-queue ETA from observed throughput, so the queue-level ETA is meaningful from the first file rather than depending on per-response sizes. `Length` is nullable, so degrade gracefully when one or more entries omit it: skip the size display for that file and fall back to a count-based estimate (or suppress the whole-queue ETA) when total bytes are not fully known, rather than failing.

Use a factory to select the reporter implementation from terminal capabilities. Add an injectable reporter factory or terminal-capability abstraction to `CliApplicationServices`; the default implementation should read stderr redirect state, CI/non-interactive environment state, and ANSI/rich-output support, while tests can inject forced rich/plain modes without depending on the real process terminal. Live progress (percentage, speed, ETA, and queue-level ETA) is an interactive-terminal-only experience: interactive stderr should use Spectre.Console progress/status primitives with a spinner for the active file and live columns/text for filename, size, percent, speed, ETA, and, for queues, file index plus total queue ETA. Non-interactive (redirected/CI) stderr does not emit live progress at all; it emits only the discrete lifecycle lines below, which already carry the useful final figures and stay greppable and non-spammy.

The non-interactive plain-text lines have a fixed, deterministic shape so tests can assert them. For queues:

```
START 3/12 alpha.bin (128.4 MB)
SUCCESS 3/12 alpha.bin -> alpha.bin (128.4 MB in 2.1s, 61.1 MB/s)
FAILED 4/12 beta.bin -> beta.bin: <error message>
SUMMARY downloaded 11/12 files (1.4 GB in 23.5s, 59.8 MB/s)
```

For non-interactive single-file downloads, use the same vocabulary without queue position:

```
START alpha.bin (128.4 MB)
SUCCESS alpha.bin (128.4 MB in 2.1s, 61.1 MB/s)
SUMMARY downloaded 1 file (128.4 MB in 2.1s, 59.8 MB/s)
```

All sizes and speeds use human-readable decimal units (1000-based: `KB`, `MB`, `GB`; speed as `MB/s`) with one decimal place. Keep this byte/speed formatting in a small, separately testable helper.

Progress updates need byte counts from the streaming download path. Add a minimal progress callback or sink to `CliDownloadSession`, `DownloadToStreamAsync`, and `DownloadToFileAsync` so the copy/decrypt loop can report durable plaintext bytes after each chunk is written. Keep the callback optional so lower-level session tests can remain focused. Compute speed and ETA from monotonic elapsed time and bytes written; avoid making tests depend on wall-clock precision by injecting a lightweight clock or keeping formatting logic separately testable.

The non-interactive single-file summary should count the selected file once the download succeeds. The queue summary should count only successfully downloaded bytes toward total throughput. Failures should be recorded with their file position and message, then processing should continue exactly as it does today. Preserve the current exit-code contract by returning `0` only when all entries succeed.

Update `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs` to cover non-interactive single-file and queue plain/log output shape, stderr-only behavior, queue failure continuation, summary contents, and exit-code preservation. Add focused unit tests for reporter mode selection and formatting if the implementation introduces separate reporter types. Keep `QueueDownloadSmokeTests` as an end-to-end reproduction check, extending it only if it can assert stderr behavior without becoming timing- or terminal-sensitive.
