# Show CLI Banner at Command Startup

> Issue: [#128](https://github.com/chA0s-Chris/ShadowDrop/issues/128)

## Rationale

The decorated ShadowDrop banner is intended to identify the CLI when a command starts, but most command handlers currently render it only immediately before successful result output. Long-running operations therefore show the banner after completing their work, while many validation and failure paths omit it entirely. Render the banner consistently at command startup without decorating machine-readable standard output.

## Acceptance Criteria

- [x] Every executable command renders the decorated banner before command-specific validation, prompts, network calls, progress reporting, and other command output.
- [x] The banner is emitted exactly once per invocation, including failure paths, once an executable command has parsed successfully; parse errors are reported without the banner, while command-specific validation runs after the banner and includes it.
- [x] Machine-readable, JSON, byte-stream, and otherwise script-consumed standard output remains undecorated.
- [x] `--no-banner` suppresses the decorated banner for every command.
- [x] Help and `--version` retain their purpose-specific output.
- [x] Comments and documentation describe the decorated banner as startup output rather than a success preamble.
- [x] Automated tests cover startup ordering for uploads, downloads, queue operations, share operations, interactive flows, suppression, and failure paths.

## Technical Details

Move decorated-banner orchestration out of the individual command handlers and into `CliApplication`, after parsing has selected an executable command and terminal capabilities have been resolved, but before command-specific validation or dispatch. Parse errors occur before this point and therefore remain banner-free, like help and version output; command-specific validation runs after the banner and therefore includes it. Route the startup banner to standard error so existing structured and redirected standard-output contracts remain intact. Keep help and version handling on their existing dedicated paths.

Remove the now-redundant `CliBannerWriter` dependency and write calls from upload, download, queue, share, and interactive handlers, or narrow the abstraction so `CliApplication` is its sole caller. Ensure interactive handlers cannot defer or duplicate rendering through delegated handlers. Update banner documentation and handler tests to reflect centralized, exactly-once startup behavior, with application-level tests verifying ordering on successful and failing invocations.
