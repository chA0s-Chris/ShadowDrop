## Rationale

Complete the first recipient CLI workflow by downloading and decrypting share files locally. This slice should support
direct share downloads, single-file selection, and queue-file-driven batch processing without exposing secrets in queue
files. The command must be non-interactive and fail clearly rather than prompt for missing required inputs.

## Acceptance Criteria

- [x] A non-interactive `shadowdrop download <share-id>` command exists.
- [x] The command accepts share key via `--share-key <key>` CLI argument.
- [x] The command supports optional `--share-key-file <path>` to read the share key from a file (CLI argument takes
  precedence if both provided).
- [x] If required share key is missing, the command exits with error (exit code 1) and does not prompt.
- [x] The command supports `--file <file-id>` for selecting one file from a multi-file share.
- [x] The command supports `--queue <path>` for queue-file-driven downloads.
- [x] Queue-file processing uses the shared JSON queue format and shared validation.
- [x] Queue files are rejected if required fields are missing or malformed.
- [x] Queue processing does not require a separate share id argument; it is embedded in the queue entry.
- [x] The CLI can download encrypted content and decrypt it locally when the share key is supplied separately.
- [x] The CLI supports optional `--bearer-token <token>` CLI argument for downloads requiring authorization; if not
  provided, the endpoint is called without Authorization header.
- [x] Bearer tokens are sourced only from CLI arguments (environment variables or files are not consulted).
- [x] The CLI never writes share keys, bearer tokens, or other secrets into queue files, logs, or stdout.
- [x] Secrets supplied as CLI arguments are cleared/disposed after use where applicable data structures support this.
- [x] Direct downloads write decrypted content to stdout and errors/status to stderr; exit code is 0 on success, 1 on
  failure.
- [x] Queue processing writes a summary report to stderr showing per-file results; exit code is 0 if all files succeed,
  1 if any file fails.
- [x] The CLI can skip or report individual queue-file failures without corrupting or stopping unrelated downloads.
- [x] Automated tests cover single-file direct download, multi-file queue processing, validation failures, local
  decryption, missing share key error, and partial queue failures.

## Queue Entry Contract

Each queue entry in a valid download queue file must contain the following fields:

- `serverUrl` (string): The base URL of the ShadowDrop server hosting the share (e.g.,
  `https://api.shadowdrop.example.com`)
- `shareId` (string): The unique identifier for the share to download
- `fileId` (string): The unique identifier of the file within the share
- `fileName` (string): The expected file name from the share manifest
- `length` (number): The expected plaintext length from the share manifest
- `outputPath` (string): The local file system path where the decrypted content should be written

Queue entries **do not and must not ever contain** share keys, bearer tokens, or other secrets. All secrets are provided
via CLI arguments only (`--share-key`, `--bearer-token`).

**Queue Validation:** Before any downloads begin, all queue entries are validated upfront. If any entry is missing
required fields or is malformed, the validation fails and the command exits with code 1. No downloads are attempted if
validation fails.

The `fileId`, `fileName`, and `length` fields intentionally duplicate share-manifest metadata so queue processing can bind each entry to exactly one manifest file and fail closed if the server-side metadata no longer matches the queued expectation.

## Technical Details

Implement the non-interactive download command in `ShadowDrop.Cli` using System.CommandLine. The command should work
both from explicit arguments and from a queue file that already contains the target server, share id, and per-file
manifest metadata (`fileId`, `fileName`, `length`). Reuse the shared queue models and validator rather than duplicating
parsing rules in the CLI.

**Share Key Input:**
The share key is required and must be provided via `--share-key <key>` (CLI argument) or `--share-key-file <path>` (read
from file).
If both are provided, the CLI argument takes precedence. If neither is provided, the command exits immediately with code
1 and an error message—no prompting or stdin fallback.

**Bearer Token Input:**
Optional bearer tokens are sourced exclusively from the `--bearer-token <token>` CLI argument. Environment variables and
config files are not consulted.
If a bearer token is supplied, it is included in the `Authorization: Bearer <token>` header. If not supplied, the
download endpoint is called without the Authorization header.

**Secrets Handling:**
Share keys and bearer tokens must never appear in queue files, logs, or any output. Secrets are never emitted in
diagnostics—not even partially masked or redacted.
Secrets supplied as CLI arguments should be cleared/disposed after use wherever the data structures support secure
cleanup (e.g., using `SecureString` or zeroing buffers).

**Direct Download Output and Exit Codes:**
Direct downloads (`shadowdrop download <share-id> --share-key <key>`) write the decrypted file content to stdout so the
output can be piped.
Errors and status messages go to stderr. Exit code is 0 on success; 1 on failure (missing key, HTTP errors, decryption
failure, etc.).

**Queue Processing Output and Exit Codes:**
When processing a queue file, the CLI validates all entries upfront against the queue entry contract. If validation
fails, the command exits immediately with code 1 before any downloads occur.
If validation succeeds, the CLI downloads files one by one.
It writes a summary report to stderr listing per-file status (success, HTTP error, decryption failure, etc.).
If any file fails, the command continues processing remaining files and exits with code 1. If all files succeed, it
exits with code 0.
This allows callers to detect partial failures and retry individual queue entries if needed.

This slice should focus on separate-key mode, which is the default sharing mode in the concept. The CLI should retrieve
encrypted content from the download endpoint and decrypt locally with the provided share key.

Keep batch behavior predictable and structured so range-aware resume support can be added cleanly later rather than
forcing a redesign of the download pipeline.
Do not add resume functionality to this slice.
