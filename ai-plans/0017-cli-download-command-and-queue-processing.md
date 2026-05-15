## Rationale

Complete the first recipient CLI workflow by downloading and decrypting share files locally. This slice should support
direct share downloads, single-file selection, and queue-file-driven batch processing without exposing secrets in queue
files.

## Acceptance Criteria

- [ ] A non-interactive `shadowdrop download <share-id>` command exists.
- [ ] The command supports `--file <file-id>` for selecting one file from a multi-file share.
- [ ] The command supports `--queue <path>` for queue-file-driven downloads.
- [ ] Queue-file processing uses the shared JSON queue format and shared validation.
- [ ] Queue files are rejected if required fields are missing or malformed.
- [ ] Queue processing does not require a separate share id argument.
- [ ] The CLI can download encrypted content and decrypt it locally when the share key is supplied separately.
- [ ] The CLI can send an optional download bearer token through `Authorization: Bearer <token>`.
- [ ] The CLI never writes admin tokens, download bearer tokens, or share keys into queue files.
- [ ] The CLI can skip or report individual queue-file failures without corrupting unrelated downloads.
- [ ] Automated tests cover single-file download, multi-file queue processing, validation failures, and local
  decryption.

## Technical Details

Implement the non-interactive download command in `ShadowDrop.Cli` using System.CommandLine. The command should work
both from explicit arguments and from a queue file that already contains the target server and share id. Reuse the
shared queue models and validator rather than duplicating parsing rules in the CLI.

This slice should focus on separate-key mode, which is the default sharing mode in the concept. The CLI should retrieve
encrypted content from the download endpoint and decrypt locally with the provided share key. If a download bearer token
is required, send it only in the `Authorization` header.

Keep batch behavior predictable. When processing a queue file, validate the full queue first, then download files one by
one with clear per-file status reporting. The implementation should be structured so range-aware resume support can be
added cleanly rather than forcing a redesign of the download pipeline.
