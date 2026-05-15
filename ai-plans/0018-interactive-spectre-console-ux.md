## Rationale

Improve manual usability by adding a guided terminal experience without sacrificing automation-friendly commands. This
slice should wrap the existing upload, share, and download capabilities in Spectre.Console-driven flows rather than
inventing separate business logic.

## Acceptance Criteria

- [ ] An interactive CLI mode exists for users who prefer a guided flow.
- [ ] The interactive mode uses Spectre.Console prompts and terminal rendering.
- [ ] The interactive flow can guide a user through uploading files and creating a share.
- [ ] The interactive flow can guide a user through choosing expiration, direct-HTTP mode, and optional download bearer
  token behavior.
- [ ] The interactive flow can display resulting link information and any separately delivered key material clearly.
- [ ] The interactive flow can guide a user through selecting and downloading files from a share.
- [ ] Interactive actions delegate to the same underlying command/use-case logic as non-interactive commands.
- [ ] Non-interactive command behavior remains available and unchanged.
- [ ] Automated tests cover the non-UI orchestration behind the interactive flows.

## Technical Details

Build the interactive layer in `ShadowDrop.Cli` with Spectre.Console prompts, selections, markup, tables, and progress
display. Do not use Spectre.Console.Cli as the command model; keep System.CommandLine as the primary parser and treat
the interactive mode as an alternate UI over the same operations.

The interactive flow should stay focused on the concept’s guided scenarios: choosing files, deciding on share
constraints, deciding whether direct HTTP mode is allowed, optionally requiring a download bearer token, and presenting
the resulting share information in a way that makes the key-handling distinction obvious. Avoid turning the CLI into a
stateful shell.

Keep core logic separate from terminal interaction so Native AOT compatibility and automated testing remain manageable.
Tests do not need to snapshot terminal cursor behavior, but they should verify the orchestration paths, validation
rules, and branching decisions that the interactive UI drives.
