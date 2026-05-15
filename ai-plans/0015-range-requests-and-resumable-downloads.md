## Rationale

Make downloads always resumable as required by the project concept. This slice should add HTTP range support on top of
the versioned chunked encryption format so interrupted downloads can continue without decrypting entire files.

## Acceptance Criteria

- [ ] The download endpoint supports single HTTP byte-range requests for file responses.
- [ ] Successful range responses use appropriate HTTP status and headers, including `206 Partial Content` and
  `Content-Range`.
- [ ] Invalid or unsatisfiable ranges are rejected with the appropriate HTTP response.
- [ ] Direct-HTTP mode can decrypt and serve only the plaintext chunks needed for the requested range.
- [ ] CLI decrypt mode can return the encrypted chunk subset needed for the requested plaintext range, or otherwise a
  response shape that preserves resumability without forcing a full-file transfer.
- [ ] Range handling does not require loading the entire encrypted file into memory.
- [ ] Range calculations respect chunk size, chunk count, and final-chunk plaintext length.
- [ ] Automated tests cover full-file requests, aligned ranges, mid-chunk ranges, multi-chunk ranges, and unsatisfiable
  ranges.

## Technical Details

Build on the shared chunked AES-256-GCM format rather than bypassing it. The server should map requested plaintext
ranges to the required encrypted chunks, then either decrypt only the needed plaintext window for direct-HTTP mode or
return the required encrypted content for CLI-side decryption in a way the CLI can consume resumably.

Support standard single-range requests first. Multipart byte ranges are unnecessary for the MVP and would add complexity
without improving the core handoff flow. Response headers and length calculations must describe the actual returned byte
window accurately so `curl -C -` and similar tooling behave predictably.

Keep the storage path streaming-oriented. The local blob backend should open a stream and read only the chunk span
required for the request. Avoid designs that require materializing whole files or all chunks up front. Tests should
explicitly prove that mid-chunk and multi-chunk requests produce the right plaintext bytes and enforce authentication
and expiration rules the same way as whole-file downloads.
