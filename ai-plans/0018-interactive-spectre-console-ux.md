## Rationale

Improve manual usability by adding a guided terminal experience without sacrificing automation-friendly commands. This
slice should wrap the existing upload, share, and download capabilities in Spectre.Console-driven flows rather than
inventing separate business logic. The interactive mode is a UI layer that delegates to the same underlying
command/use-case
operations already delivered or concurrently delivered in plans 0016 (upload), 0013 (share creation), and 0017 (
download).

## Acceptance Criteria

- [ ] An interactive CLI mode exists that is invoked with an explicit `--interactive` flag or equivalent subcommand
  structure, making the mode unambiguous and orthogonal to non-interactive System.CommandLine parsing.
- [ ] The interactive mode uses Spectre.Console prompts and terminal rendering.
- [ ] The interactive flow can guide a user through uploading files and creating a share.
- [ ] The interactive flow can guide a user through choosing expiration, direct-HTTP mode, and optional download bearer
  token behavior.
- [ ] The interactive flow can display resulting link information and any separately delivered key material clearly.
- [ ] The interactive flow can guide a user through selecting and downloading files from a share.
- [ ] Interactive actions delegate to the same underlying command/use-case logic as non-interactive commands.
- [ ] Non-interactive command behavior remains available and unchanged.
- [ ] When invoked in a non-TTY environment or unsupported terminal, the interactive mode fails immediately with a clear
  error message indicating TTY requirement; it does not fall back to non-interactive behavior.
- [ ] Interactive mode enforces the same secret-handling guarantees as non-interactive commands: share keys shown only
  on explicit opt-in (`--output-secret` flag or equivalent), token input masked in terminal prompts, secrets never
  rendered in normal diagnostics/output, no weakening of prior secret-lifecycle rules from upload/download/share plans.
- [ ] Automated tests cover the non-UI orchestration behind the interactive flows, including secret-handling paths.
- [ ] Orchestration and output tests explicitly assert that secrets (share keys, bearer tokens, plaintext key material)
  do not appear in rendered stderr or log output.

## Technical Details

### Interactive Mode Invocation

The interactive mode is invoked with an explicit `--interactive` flag on the subcommand as a command-level option.
This prevents ambiguity in the System.CommandLine argument parser and keeps the mode orthogonal to existing
non-interactive
commands. Example invocation: `shadowdrop upload --interactive` or `shadowdrop download --interactive`.
The `--interactive` flag is placed after the subcommand (upload, download, share create, etc.) and must be documented
clearly in the implementation and help text so users understand when they are entering guided mode.

### TTY and Terminal Environment Requirements

Interactive mode requires a TTY (terminal) for Spectre.Console rendering. Before entering the interactive flow, the
implementation must check whether the console supports interactive I/O. If running in a non-TTY environment (e.g., piped
input, containerized without TTY allocation, or CI/CD system without pseudo-terminal), the command must fail immediately
with exit code 1 and a clear error message: **"Interactive mode requires a terminal. Use non-interactive commands with
explicit flags for scripted or piped environments."** Do not attempt to fall back to non-interactive prompts or other
workarounds.

### Delegation to Underlying Non-Interactive Flows

The interactive layer is a UI wrapper only. All business logic—file encryption, share creation, token hashing, download
decryption—comes from the underlying non-interactive commands delivered or concurrently delivered in plans 0016 (
upload),
0013 (share creation), and 0017 (download). The interactive mode must invoke the same validation rules, error handling,
and
operations as if the user had supplied the equivalent non-interactive command-line flags manually. Do not duplicate or
reimplement business logic in the interactive flow.

#### Guided Share-Creation Workflow

The interactive share-creation flow carries the upload-generated share key securely through the guided session:

1. **Upload & Key Generation (from plan 0016):** The user selects files to upload. The interactive mode delegates to the
   underlying upload operation, which generates an ephemeral or persisted share key according to plan 0016 rules.
2. **Key Flow into Guided Prompts:** The generated share key is held in memory as an opaque secret and passed to the
   share
   creation orchestration (plan 0013 logic) without rendering it to the terminal at this stage.
3. **Share Configuration:** The interactive flow guides the user through share options: expiration, direct-HTTP mode,
   optional
   download bearer token (see below), and confirmation. All choices are captured and translated into non-interactive
   command
   arguments.
4. **Share-Mode Constraint Enforcement:** Per plan 0013, the interactive layer enforces invalid mode/token combinations
   at
   prompt time:
  - **Direct-HTTP mode with optional bearer token:** Rejected. Direct-HTTP shares have no authentication; a bearer token
    cannot
    be enforced. If the user selects direct-HTTP mode, the flow must skip or disable the bearer-token option.
  - **Separate-key mode without bearer-token setup:** Rejected. If the user chooses separate-key mode (not direct-HTTP),
    they
    must configure an optional download bearer token (either enabled or explicitly declined, but not omitted). This
    ensures
    the guided flow enforces the same invariants as the non-interactive API.
5. **Secret Output Control:** After share creation completes, the share key is displayed to the user only if they
   explicitly
   opt-in (via a yes/no prompt or `--output-secret` equivalent flag). If they decline, the key is discarded. If they
   accept,
   it is shown clearly and separately, not buried in diagnostic output.
6. **Delegation:** All share-creation business logic (token hashing, permission checks, server calls) is delegated to
   plan
   0013 operations. The interactive layer only orchestrates prompts and aggregates user responses.

#### Optional Download Bearer-Token Prompting in Share Creation

When the user enables optional download bearer-token protection as part of the guided share-creation flow:

1. **Two-Step Bearer-Token Decision:**
  - **Step 1 (Opt-In):** Prompt the user with a yes/no question: "Do you want to require a download bearer token?" (or
    equivalent). This is the first decision point where the user decides whether token protection is required.
  - **Step 2 (Token Entry):** If yes, prompt the user to enter the bearer token using Spectre.Console's masked-input
    prompt (password-entry style) so the token is not echoed to the terminal. If no, skip token entry and proceed
    without optional token protection.

2. **Bearer-Token Source Boundaries:** Bearer tokens entered in interactive mode come **only** from direct masked
   terminal input. No environment variables, config files, or other input sources are used for bearer-token entry in
   interactive mode. This ensures the user retains explicit control and visibility (albeit masked) of token input.

3. **Masking and Input Handling:** The masked-input prompt must not echo characters to the terminal, and must handle
   backspace and standard terminal editing correctly.

4. **Single Session Scope:** The token is captured, hashed (per plan 0013), and passed to the underlying share-creation
   operation. It is not persisted or cached beyond the current command invocation.

5. **Delegation:** Token hashing and validation logic remain in plan 0013 operations. The interactive layer only
   presents the prompt, masks input, and passes the plaintext token to the underlying share-creation orchestration.

6. **Secrets in Output:** The plaintext token is never logged, rendered in normal output, or included in diagnostic
   messages. Only hashed tokens (if logged at all) follow existing secret-handling rules.

#### Guided Download Workflow and Share-Key Input Contract

The interactive download flow guides users through selecting files to download from a share, with careful handling of
share keys and bearer tokens:

1. **Share-Key Input Precedence:** During guided download, the interactive layer must follow this precedence for
   share-key input:
  - **Pre-supplied via command-line flags:** If the user provides `--share-key` or `--share-key-file` as non-interactive
    command-line arguments in addition to `--interactive`, those values take precedence and are used directly without
    re-prompting. This allows hybrid workflows where users supply sensitive inputs via flags/files for automation, then
    enter guided mode for file selection.
  - **Prompted if not pre-supplied:** If no `--share-key` or `--share-key-file` is provided, the interactive flow
    prompts the user to enter the share key using a Spectre.Console masked-input prompt (password-entry style), ensuring
    the key is not echoed to the terminal.
  - **Single Entry:** The share key is entered once per download command invocation. It is not re-prompted or cached
    across invocations.

2. **Bearer-Token Input During Download:** If the share requires a download bearer token:
  - The interactive flow must prompt for the bearer token using a Spectre.Console masked-input prompt (password-entry
    style).
  - Like share keys, bearer tokens may be pre-supplied via `--bearer-token` or similar command-line flags (if such flags
    exist per plan 0017); if provided, they take precedence over interactive prompting.
  - If no pre-supplied bearer token is available and one is required, the interactive flow prompts the user to enter it
    masked.

3. **File Selection and Download:** After share-key and token validation, the interactive flow guides the user through
   selecting files to download from the available share contents, delegating the actual download operation to plan 0017
   logic.

4. **Secrets in Download Output:** Share keys and bearer tokens used during interactive download follow the same
   secret-handling rules as share creation: plaintext values are never logged or rendered in normal output, only used
   for cryptographic validation/decryption operations.

### Secret Handling and Security Guarantees

Interactive mode **inherits and strengthens** all secret-handling rules from prior upload, share-creation, and download
plans:

- **Share keys:** Display to the user only when they explicitly opt-in (e.g., `--output-secret` flag or an affirmative
  prompt choice like "Do you want to display the share key now?"). By default, keys are not rendered. If displayed, the
  share key is emitted to stdout only in plaintext form—no automatic "copy to clipboard" or "save to file" mechanisms
  are implemented in this slice. Users may use terminal multiplexing, shell redirection, or external clipboard tools
  at their own discretion; the CLI does not provide convenience wrappers that could introduce new secret-exposure
  vectors.
- **Token input:** When prompting for share keys, bearer tokens, or admin tokens, Spectre.Console must use masked input
  prompts (password-entry style) so characters are not echoed to the terminal.
- **Secrets in output:** Never render plaintext share keys, bearer tokens, admin tokens, or other secrets in normal
  diagnostics, error messages, warnings, or progress output. If a user requests secret display (opt-in), emit it clearly
  and separately, not buried in diagnostic output.
- **Logging and test output:** Secrets are never logged even at diagnostic verbosity levels. Ensure that Spectre
  rendering,
  progress bars, and any debug output do not accidentally emit secret material. Tests must explicitly assert the absence
  of secrets from stderr/log captures using grep or equivalent (see testing requirement below).
- **No weakening:** Do not add any convenience that relaxes the secret-lifecycle rules. If a later requirement suggests
  caching tokens or emitting keys by default for usability, reject it; prefer explicit user opt-in and clear warnings
  instead.

### Scope Boundaries

Keep the interactive layer focused on **guided UX over existing operations**. Do not:

- Implement a stateful shell where users stay inside a long-lived REPL issuing commands.
- Add new business logic (e.g., new encryption algorithms, server-side features, batch orchestration).
- Persist interactive-mode state (e.g., "remember my last server URL" across sessions) beyond the current command
  invocation.

If a feature request demands state or new logic, evaluate it as a separate plan rather than grafting it into this
interactive layer.

### Testing Requirements

Design tests so they exercise orchestration independently of terminal rendering:

- **Orchestration tests:** Mock the Spectre.Console I/O to simulate user choices (file selections, yes/no answers,
  numeric inputs). Verify that the business logic correctly delegates to the underlying upload, share-creation, and
  download operations with the right arguments and secret handling.
- **Secret assertion:** Include an explicit test criterion that stderr, log files, and rendered output captures do **not
  **
  contain plaintext share keys, bearer tokens, admin tokens, or other secrets. Use regex/grep assertions to catch
  accidental leaks.
- **No snapshot bloat:** Tests do not need to snapshot terminal cursor positions, colors, or box-drawing characters;
  focus
  on orchestration paths, validation branches, and secret confidentiality.
- **AOT compatibility:** Design the interactive layer (Spectre.Console integration, user input handling, output
  rendering)
  to remain compatible with Native AOT compilation, as required by plan 0020.
