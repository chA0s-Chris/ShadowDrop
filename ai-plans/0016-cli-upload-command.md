## Rationale

Add the first end-user CLI workflow so senders can upload files with client-side encryption from scripts and terminals.
This slice should prove the CLI can read local files, encrypt them chunk-by-chunk, and call the protected upload API
non-interactively.

## Acceptance Criteria

- [ ] A non-interactive `shadowdrop upload` command exists.
- [ ] The command accepts one or more local file paths.
- [ ] The command resolves server URL and admin token from flags, environment variables, or config.
- [ ] The command encrypts files client-side using the shared chunked AES-256-GCM implementation.
- [ ] The command streams encrypted content to the upload API instead of buffering the full plaintext file in memory.
- [ ] The command sends only encrypted content and non-secret metadata to the API.
- [ ] The command outputs the created file ids in a script-friendly format.
- [ ] The command produces clear validation errors for missing files, unreadable files, missing server URL, and missing
  admin token.
- [ ] The command remains compatible with Native AOT.
- [ ] Automated tests cover command parsing, configuration precedence, and upload workflow behavior against a test
  server.

## Technical Details

Implement the command in `ShadowDrop.Cli` with System.CommandLine. Keep the command non-interactive and
automation-friendly: explicit arguments and flags first, with config and environment variable fallback. Spectre.Console
may be used for terminal rendering, but this slice should not depend on wizard-style prompts.

Use `ShadowDrop.Shared` for the encryption format and shared contracts. The CLI should generate the share-level
encryption secret client-side, derive per-file keys as needed, and upload encrypted bytes plus metadata only. This
upload command does not need to create a share yet; it should stop after file intake and report the resulting file ids.

Design the HTTP client path and serializer choices with Native AOT in mind. Prefer explicit JSON contracts and
source-generation-friendly patterns over reflection-heavy convenience APIs. Tests can use an in-process API host or
equivalent test server to verify the CLI sends the expected metadata and handles API failures cleanly.
