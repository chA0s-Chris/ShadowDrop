## Rationale

Issue [#116](https://github.com/chA0s-Chris/ShadowDrop/issues/116) changes the single-file CLI download workflow so recipients get a file on disk by default instead of decrypted bytes streamed to stdout. The server already exposes recipient-facing filename metadata through the share manifest and download response headers, so the CLI can choose a practical default output path without requiring shell redirection for the common case.

This should be a deliberate contract change for single-file downloads only. Queue downloads keep their existing queue `outputPath` and `--output-root` behavior, while progress and normal status output can move to stdout now that stdout is no longer reserved for file bytes. Errors and diagnostics continue to use stderr.

Unlike the upload commands, which reserve stdout for a parseable `key:value` contract, download stdout is human-readable free-form output with no machine contract. Moving queue download progress from stderr to stdout is a breaking change for scripts that today rely on the two streams being separated.

## Acceptance Criteria

- [ ] Single-file CLI downloads write to `./<original-filename>` by default.
- [ ] When the manifest announces no usable filename, the default output path falls back to `./<fileId>`, or `./download.bin` when the fileId is also absent.
- [ ] Single-file CLI downloads support `--out <file-path>` for an explicit destination file.
- [ ] Single-file CLI downloads support `--out <existing-directory>` for a directory destination using the original filename.
- [ ] `--out` with a trailing directory separator is treated as a directory destination whether or not the directory exists, creating it when missing.
- [ ] If `--out` has no trailing separator and does not name an existing directory, it is treated as an explicit file path.
- [ ] For explicit output file paths, the CLI creates all missing parent directories and fails with a non-zero exit code if parent directory creation fails.
- [ ] `--out` accepts an absolute path or a relative path containing `..`, writing to exactly the location the user named.
- [ ] `--out` combined with `--queue` fails with a non-zero exit code and an error directing the user to `--output-root`.
- [ ] Single-file CLI downloads no longer write decrypted file bytes to stdout implicitly.
- [ ] Download progress and normal status output uses stdout for both single-file and queue downloads, in plain-text and rich modes alike.
- [ ] Interactive prompts, guided download summaries, and banner output remain on stderr.
- [ ] Download errors, including per-item failure lines, remain on stderr.
- [ ] A manifest `fileName` containing a path separator (`/` or `\`) resolves to its leaf name inside the intended output directory.
- [ ] A manifest `fileName` of `.` or `..`, or one that sanitizes to an empty name, fails with a non-zero exit code and an error on stderr.
- [ ] An existing destination file that matches the shared file by length and `plaintextSha256` is accepted as already downloaded, and the command exits zero.
- [ ] When the manifest omits `plaintextSha256`, an existing destination file matching by length alone is accepted as already downloaded.
- [ ] An existing destination file that does not match the shared file causes a non-zero exit with an error on stderr, and the existing file is left untouched.
- [ ] Interrupted single-file downloads still resume from an existing `.partial` file and resume marker.
- [ ] Queue downloads keep queue `outputPath` and `--output-root` semantics.
- [ ] Queue and interactive downloads continue to commit their output successfully when no destination file exists at commit time.
- [ ] `docs/CLI.md` and `README.md` are updated to remove shell redirection as the normal single-file download flow, showing default file output, explicit file output, and directory output instead.
- [ ] `docs/CLI.md` documents that download progress and status output moved from stderr to stdout, calling it out as a breaking change for scripts.
- [ ] Tests cover default output path, announced-filename fallback, explicit output file path, existing-directory `--out`, trailing-separator `--out`, absolute and `..`-containing `--out`, `--out` with `--queue`, existing-file match and mismatch, unsafe filename handling, and stdout/stderr behavior.
- [ ] An end-to-end smoke test asserts a single-file download lands at the default path.

## Technical Details

Update the single-file download path in the CLI so it resolves an output file before starting content download. The load-bearing changes are expected around `CliApplication`, `DownloadCommandOptions`, `DownloadCommandHandler`, `InteractiveDownloadCommandHandler`, `DownloadProgressReporterFactory`, `PlainTextDownloadProgressReporter`, `SpectreDownloadProgressReporter`, and the existing CLI download/progress tests. Prefer the manifest `fileName` as the source of the original filename because it is available before the file bytes are requested; use `X-ShadowDrop-File-Name` or direct-HTTP `Content-Disposition` only as a consistency check or fallback if that fits the existing download layering. Keep filename handling centralized so every default filename path goes through the same validation.

The download command has no `--out` option today; the only existing `--out` belongs to `queue create`. Add a new option bound as `Option<String>` rather than `Option<FileInfo>`, because `FileInfo` normalizes away the trailing directory separator that the destination resolver needs to see, and extend the `DownloadCommandOptions` record with a `String? Out` member. Reject `--out` together with `--queue` during option validation.

Introduce a small destination resolver for single-file downloads. With no `--out`, resolve to the current working directory plus the sanitized announced filename. With `--out`, inspect the raw argument: a trailing directory separator, or a value naming an existing directory, means directory semantics — create the directory if missing and append the sanitized announced filename. Otherwise treat the value as an explicit file path, preserving the user's exact filename and creating missing parent directories. The user's `--out` value is honored as given, including relative traversal and absolute paths; only the server-announced filename is sanitized, so no server metadata can introduce path segments. When `--out` names a directory, the resolved destination must remain inside it after the announced filename is appended.

Sanitize server-provided filenames before using them in filesystem paths. `QueueFileBuilder.Sanitize` already implements exactly the required rules — leaf-only extraction that also handles `\` on non-Windows platforms, invalid-character replacement, `.`/`..` rejection, and reserved-device-name prefixing. Extract it into a shared internal helper (for example `ShadowDrop.Cli.Files.SafeFileName`), generalizing its `QueueBuildException` into something both call sites can use, and route the queue builder, the single-file resolver, and `InteractiveDownloadCommandHandler.ResolveOutputFileName` through it. That last one currently derives names with a bare `Path.GetFileName` and bypasses sanitization entirely. Do not allow server metadata to create nested paths.

Reuse the existing `DownloadCommandHandler.DownloadToFileAsync` for single-file downloads rather than adding a new write path; it already backs both queue and interactive downloads, and reusing it means single-file downloads inherit `.partial` plus resume-marker semantics for free. Pass the manifest's `plaintextSha256` as `expectedPlaintextSha256` so the existing-output comparison is not reduced to a length-only check. Overwrite protection already exists there via `TrySkipCompletedOutputAsync`, which throws on a mismatched existing file and reports an identical one as already downloaded; the only change needed is switching the final commit to `File.Move(partialPath, outputPath, overwrite: false)` to close the time-of-check-to-time-of-use window. That commit is shared, so the stricter flag applies to every caller of `DownloadToFileAsync` — single-file, queue, and interactive downloads alike. This is intended: each caller already rejects a mismatched existing file before the download
starts, so the only case the flag can now fail is a destination that appeared mid-download. Do not introduce atomic-create semantics on the destination path itself, as that would defeat resume.

Move progress and normal status output for both single-file and queue downloads to stdout, in plain-text and rich modes alike. `DownloadProgressReporterFactory` and `PlainDownloadProgressReporterFactory` currently take a single `standardError` writer, gate on `ITerminalCapabilityProvider.DetectForStandardError()`, and hand that writer to `AnsiConsoleOutput`; switch both factories' writer, the capability gate, and the `AnsiConsoleOutput` writer to stdout and `DetectForStandardOutput()`, which already exists on the interface. Reporters take both writers, `(TextWriter standardOut, TextWriter standardError)`, emitting lifecycle and summary lines on stdout and per-item `FAILED` lines on stderr. Keep exceptions, diagnostic failure messages, interactive prompts, the guided download summary, and banner output on stderr. Because stdout no longer carries decrypted bytes, remove the implicit stdout streaming behavior from the single-file command instead of adding a replacement streaming mode.
That leaves `DownloadCommandHandler`'s `standardOutStream` constructor parameter unused; remove it along with its `CliApplication` wiring. Keep `DownloadToStreamAsync` itself, which `DownloadToFileAsync` still calls.

Update documentation, command help, and examples in `docs/CLI.md` and `README.md` that currently show shell redirection as the normal download path. Examples should demonstrate default file output, explicit file output, and directory output.

Add focused automated tests around the destination resolver, filename safety, existing-output match and mismatch behavior, and stream usage. Include command-level tests that verify single-file downloads write to the expected file, stdout contains progress or status rather than file bytes, errors remain on stderr, and queue download output behavior still respects queue paths and output roots.
