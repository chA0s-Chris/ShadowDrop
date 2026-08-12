# Add recursive upload selection, input lists, and dry runs

> Issue: [#207](https://github.com/chA0s-Chris/ShadowDrop/issues/207)

## Rationale

ShadowDrop can upload multiple explicitly named files and preserve their relative paths in generated queues, but it cannot recursively select directories, filter discovered files, consume reusable or streamed path lists, or preview an upload plan without remote side effects.

Introduce a single local planning model shared by `upload`, `upload raw`, and dry-run. Selection decides which files enter the plan; the existing `--input-root` and `--flatten` behavior remains responsible for generated queue destinations. The feature is cohesive but broad, so stacked delivery lets reviewers validate the behavior-preserving planner refactor before reviewing recursive and listed inputs, followed by the dependent dry-run contract.

## Acceptance Criteria

### Layer 1: Shared upload planning

- [x] `upload` and `upload raw` consume a shared, immutable local upload plan that contains the resolved files, deterministic file numbers, plaintext lengths, chunk counts, and encrypted lengths.
- [x] Existing explicit-file invocations that pass local planning retain their current file ordering, diagnostics, progress reporting, metadata, queue destinations, and upload outcomes.
- [x] The complete selected batch is preflighted before configuration resolution, prospective-output validation, or any remote request; missing, empty, unreadable, duplicate, or unrepresentably large files prevent all uploads. Local failures are reported in the precedence order defined in Technical Details, so an invocation containing several errors reports the earliest stage.
- [x] Duplicate source paths use the existing OS-aware comparison: case-insensitive on Windows and ordinal elsewhere.
- [x] Real uploads consume the prepared plan without recomputing selection or encrypted sizes, while retaining the existing file-length revalidation immediately before reserving each file ID.
- [x] Automated tests cover the shared planner, prove that refactoring explicit-file uploads does not change observable `upload` or `upload raw` behavior after local planning succeeds, and verify local selection and preflight error precedence.

### Layer 2: Recursive and listed inputs

- [ ] Both upload commands accept `-r`/`--recursive`, repeatable `-i`/`--include <glob>`, repeatable `-x`/`--exclude <glob>`, and repeatable `--files-from <file|->`; positional help describes operands as input paths, and the include/exclude help states directory-relative matching and that exclusion wins.
- [ ] Either command may obtain all inputs from `--files-from`, without requiring a positional operand.
- [ ] Directory operands require `--recursive`; otherwise the command names the directory, recommends `--recursive`, and performs no remote or output side effect.
- [ ] Recursive discovery includes hidden content, does not apply ignore files, does not traverse directory symlinks or reparse-point directories, and fails rather than silently skipping inaccessible input. Explicitly supplied file operands keep their current link behavior.
- [ ] Files discovered beneath each directory are ordered deterministically by their normalized directory-relative paths; positional inputs come first, followed by file-list sources in option occurrence order, while each list retains record order.
- [ ] Include and exclude patterns require `--recursive`, match the entire `/`-separated path relative to each traversed directory operand, support the `*`, `?`, and `**` semantics defined in Technical Details, and follow the platform's source-path case convention.
- [ ] With no includes, every discovered file is initially selected; repeated includes are ORed, repeated excludes are ORed, and exclusion takes precedence.
- [ ] Filters apply only to recursively discovered files. Explicit file operands from either the command line or a file list bypass filters.
- [ ] `--files-from` reads strict UTF-8 with one path per line, ignores only empty records, preserves all other whitespace, and performs no comment, quoting, escaping, or environment-variable interpretation.
- [ ] Relative positional and listed paths resolve against the command's captured initial working directory; invalid listed paths identify their source file or stdin and record number.
- [ ] `--files-from -` reads the injected standard input, may appear once, and cannot be combined with `--interactive`.
- [ ] Empty expansions, invalid glob patterns, malformed UTF-8, unreadable lists, directory enumeration failures, display-name errors, and queue-destination conflicts are reported before configuration resolution, output validation, or network access. Recursively discovered and listed files enter the existing duplicate check, so overlapping trees and repeated list records are rejected on the same terms as repeated operands.
- [ ] `--name` requires exactly one resolved file, and `--display-name` mappings resolve against the expanded selection.
- [ ] Interactive upload applies the same resolver to supplied paths while preserving its existing prompted file selection when no inputs are supplied.
- [ ] Recursive selection does not alter server-side file-name metadata or queue v2; `--input-root` continues to choose the preserved queue root, while `--flatten` continues to discard source directories and resolve destination collisions deterministically.
- [ ] CLI tests cover aliases, repeated filters, matching and precedence, multiple roots, hidden files, directory links, inaccessible paths, ordering, overlapping selections, input files, stdin, Unicode, whitespace, diagnostics, interactive conflicts, display names, and queue destinations.

### Layer 3: Dry-run, documentation, and end-to-end behavior

- [ ] `upload` and `upload raw` accept `--dry-run` and produce the same resolved local plan that a corresponding real invocation consumes.
- [ ] Dry-run performs every validation that the corresponding real command can complete from command arguments and local state, including recursive expansion and filtering, duplicate detection, file preflight and encrypted-size calculation, share options, direct-HTTP restrictions, queue and secrets option combinations, `--embed-secrets` requirements, display-name mappings, prospective output validation, and applicable queue-destination planning.
- [ ] Dry-run bypasses server configuration, TLS client creation, upload capabilities, authentication, reservations, uploads, share creation, queue fetching, and the automatic update check. Only checks requiring configuration, TLS/HTTP construction, authentication, server capabilities, quotas, or other remote state are reported as unchecked.
- [ ] Dry-run creates or overwrites no queue, secrets, cache, or other output file; `--force` and prospective output options are validated and reported without mutation.
- [ ] Plain output reports every selected absolute source path, its plaintext and encrypted sizes, its queue destination when applicable, aggregate selected/excluded counts and byte totals, intended output paths, and the server-side checks that were not performed.
- [ ] `--dry-run --json` emits exactly one Native-AOT-compatible result object for successful or failed local validation, using the stable camelCase schema and success/failure rules defined in Technical Details.
- [ ] A valid non-empty plan exits zero; local validation failures exit non-zero. Excluded-file totals count regular files rejected by filters rather than skipped directory links.
- [ ] `--dry-run` cannot be combined with `--interactive`; interactive upload retains its existing confirmation summary, while dry-run remains a deterministic non-interactive contract.
- [ ] Documentation covers recursive selection, glob semantics, ordering, hidden content, link handling, input-list encoding and stdin, queue-path interaction, dry-run output and limitations, and the risk of recursively selecting secrets.
- [ ] Unit tests prove that dry-run makes no HTTP, update-check, or output-write calls and that its files, ordering, sizes, display names, and destinations match real execution. The plain-text and JSON contracts are covered for `upload` and for `upload raw`, whose result omits queue destinations and display names.
- [ ] Real-process end-to-end coverage recursively uploads a filtered directory, generates a queue, downloads it, and verifies the expected nested files byte-for-byte.

## Technical Details

### Stack Design

1. `0207-add-recursive-upload-selection-input-planner` — establish and test the shared input/preflight model without changing the public CLI; verify existing non-E2E upload behavior.
2. `0207-add-recursive-upload-selection-input-cli` — expose recursive discovery, filters, and listed inputs through `upload` and `upload raw`; depends on the planner and verifies the public selection and queue interactions.
3. `0207-add-recursive-upload-selection-input-dry-run` — add local preview contracts, documentation, and cumulative real-process coverage; depends on the complete selection interface and verifies the full feature.

Introduce new upload-planning types under `ShadowDrop.Cli.Uploads`. The existing `UploadFilePlan` remains the per-file reservation record; name the new selection-time types so the two plan concepts stay distinguishable. Move the locally deterministic portion of `UploadCommandExecutor.PreflightFiles` into that boundary, leaving server-capability enforcement and per-file upload execution in the executor. Each selected entry should retain its normalized full path, selection origin, optional directory-relative match path, stable file number, and immutable size snapshot.

Local validation reports the first failing stage in this order: option-combination validation; input-list reading and glob-pattern validation; recursive expansion and empty-selection detection; duplicate detection; file preflight; display-name resolution; queue-destination planning; share-option validation; prospective-output validation. Configuration resolution, TLS/HTTP construction, and remote requests follow all of them.

Use an AOT-compatible glob matcher against each complete directory-relative path after normalizing separators to `/`. `*` matches zero or more characters within one segment, `?` matches exactly one character within one segment, and `**` is recursive only as a complete segment and matches zero or more segments, so `**/*.pdf` matches both `report.pdf` and `docs/report.pdf`. Wildcards match leading dots. `\*`, `\?`, and `\\` escape literal characters; other or trailing escapes, and multiple-star runs other than a complete `**` segment, are invalid. The match path excludes the directory operand's own leaf name, so `-r docs -i '**/*.pdf'` matches the file whose queue destination is `docs/readme.pdf`. Matching is ordinal case-insensitive on Windows and ordinal case-sensitive elsewhere. Apply patterns to regular files after traversal rather than pruning directory enumeration; identify an invalid pattern and its option before configuration resolution. Enumerate without following directory
links, materialize the complete result before downstream validation, and do not consult `.gitignore` or similar files.

Extend `CliApplicationServices` with a `TextReader` for standard input, defaulting to `Console.In`, so stdin behavior is testable without changing global console state. Decode file lists with strict UTF-8 error handling. Capture the working directory once in `CliApplication` and use it for positional paths, listed paths, match roots, display-name mappings, and queue planning.

Keep selection and destination planning distinct. `QueueDestinationResolver` continues to derive destinations from the fully expanded `FileInfo` sequence and existing display-name map; no uploader paths enter upload metadata or the queue contract.

Add a separate dry-run result contract under `ShadowDrop.Cli.Results` and register it with `CliJsonSerializerContext`. The exact camelCase JSON field contract is:

- `status`: `"valid"` or `"invalid"`.
- `files`: an array of `{ sourcePath: string, plaintextBytes: integer, encryptedBytes: integer, queueDestination: string | null }` objects.
- `totals`: `{ selectedFiles: integer, excludedFiles: integer, plaintextBytes: integer, encryptedBytes: integer }`.
- `intendedOutputs`: `{ queueFile: string | null, secretsFile: string | null }`.
- `uncheckedValidations`: the stable string values `serverAvailability`, `authentication`, `uploadCapabilities`, `accountQuota`, and `serverFileSizeLimit`, in that order.
- `errors`: an array of `{ message: string, source: string | null, recordNumber: integer | null }` objects.

All properties are always present. A valid result has an empty `errors` array. An invalid result has at least one error and exposes no partial plan: `files` is empty, every total is zero, and both intended-output paths are null. Error `source` is `commandLine`, `stdin`, or the absolute path of a list file; `recordNumber` is one-based for stdin and list records and null for command-line errors. Once command parsing succeeds, JSON dry-run failures still produce this structured result.

Recognize dry-run before the current TLS/HTTP dispatch path and suppress the post-command automatic update check, ensuring the no-network promise covers the whole process rather than only the upload handler. Reuse the read-only checks in `AtomicFileWriter.EnsureWritable`, but never call its writer.

Out of scope remain ignore-file integration, following directory links, NUL-delimited `--files0-from`, archive uploads, and any queue-format or manifest-path change.
