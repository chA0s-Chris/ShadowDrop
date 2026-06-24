## Rationale

Queue-based CLI downloads should be portable and locally contained. Today, queue entry `outputPath` values are passed
directly to the file writer, so relative paths resolve against the process current working directory and absolute paths
are accepted. Add an explicit queue-download output root so callers can choose the destination directory without
changing directories, and reject absolute queue paths so received queue files cannot prescribe arbitrary filesystem
locations. This plan addresses [#52](https://github.com/chA0s-Chris/ShadowDrop/issues/52).

## Acceptance Criteria

- [x] `shadowdrop download --queue <path>` accepts an optional output-root option for queue downloads.
- [x] When the output-root option is omitted, existing relative queue paths continue to resolve under the current
  working directory.
- [x] Queue entry `outputPath` values must be relative paths; absolute path forms such as `/x`, `C:\x`, `C:/x`, and
  `\\server\share\x` are rejected as invalid queue content regardless of host OS.
- [x] Queue downloads resolve each `outputPath` under the selected output root and reject paths that escape that root
  after normalization.
- [x] Supplying the output-root option without `--queue` fails with a clear validation error and does not start a direct
  or interactive download.
- [x] Generated queue files continue to use sanitized, collision-safe relative output paths.
- [x] Automated tests are written for explicit output roots, the default current-directory behavior, absolute path
  rejection, traversal rejection, and unchanged queue generation.

## Technical Details

Extend the CLI download options and command wiring with a queue-only output-root option, likely named `--output-root`.
Reject combinations where this option is supplied without `--queue`, because direct downloads currently stream to stdout
and do not write files through queue entry paths. This option is scoped to queue downloads only; the interactive download
path (`InteractiveDownloadCommandHandler`) is unaffected. Containment is enforced lexically against the normalized root and
does not resolve symlinks, so a symlinked directory inside the root is out of scope for this work.

Keep queue files themselves portable by preserving `QueueFileBuilder` behavior: generated `outputPath` values should
remain sanitized relative names with deterministic collision handling. Strengthen `QueueFileParser.Validate` so
absolute `outputPath` values are validation errors. This keeps unsafe queue files from reaching download execution and
gives callers the same error path used for other malformed queue content. Absolute-form detection must be OS-independent
rather than delegated to `Path.IsPathRooted` (which is host-dependent — e.g. `C:\x` is a valid relative name on Linux), so
the portable absolute forms are rejected identically on every host platform.

During `DownloadCommandHandler.ExecuteQueueAsync`, resolve the selected output root to a full path before processing
entries. For each queue entry, combine the root with the relative `outputPath`, normalize the combined path, and verify
that the result remains inside the normalized root. Use the resolved full destination path when calling
`DownloadToFileAsync`, but keep queue status messages clear enough that users can see which queue entry succeeded or
failed. The containment check should handle directory separator boundaries correctly so similarly prefixed sibling
directories are not accepted accidentally.

Tests should cover both validation and execution. Parser tests should reject portable absolute-path examples on every
host platform, including `/x`, `C:\x`, `C:/x`, and `\\server\share\x`. CLI download tests should verify that an explicit
output root writes files below that root, that omitting the option preserves current-working-directory behavior, and
that traversal such as `../outside.bin` fails before writing outside the root.
