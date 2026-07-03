# Close the CLI download truncation gap and harden the resume marker

> Issue: [#98](https://github.com/chA0s-Chris/ShadowDrop/issues/98) (part of umbrella [#97](https://github.com/chA0s-Chris/ShadowDrop/issues/97))

## Rationale

This closes findings 1 and 2 of the pre-release review (umbrella issue #97), and is the one
the v1 release is gated on. It closes the review's finding 1 (a malicious server can silently
truncate a CLI download at a chunk boundary) and finding 2 (the resume marker persists the
share token with default permissions), and folds in the three low-risk correctness notes
from the same review because they live in the same download/crypto code.

Finding 1 is the important one. The chunk AAD (`ChunkEncryptionService.BuildAad`) binds
version, algorithm, file id, chunk size, chunk index, and plaintext chunk length, but it
does **not** bind whether a chunk is the last chunk of the file. So a server can serve the
first `k` original full-sized chunks (`0..k-1`) and declare a total plaintext size of
`k * ChunkSize`; the CLI then treats chunk `k-1` as the final chunk with a full-sized
plaintext length — exactly the AAD that chunk was really encrypted under — so every chunk
authenticates and the recipient silently receives a truncated file. Per-chunk GCM integrity
cannot detect this because nothing in the authenticated data distinguishes a genuine final
chunk from a re-labelled middle chunk.

The fix binds an `isFinal` marker into the AAD so a re-labelled middle chunk fails
authentication. Because ShadowDrop is unreleased, the crypto format can change freely: no
`EncryptionFormatVersion` bump and no backward-compatibility path are required. A
post-download length/SHA-256 verification is added as defense-in-depth for the primary
download path, which currently verifies nothing about the bytes it produces.

## Acceptance Criteria

- [x] The chunk AAD binds a final-chunk marker (`isFinal`), so a chunk encrypted as a
  non-final chunk fails authentication when presented as the final chunk, and vice versa.
- [x] A server that serves the first `k` full-sized chunks of a longer file and declares the
  total plaintext size as `k * ChunkSize` causes the CLI download to fail with a decryption
  error instead of writing a truncated file.
- [x] Upload encryption sets `isFinal` on the last chunk only, including when the plaintext
  length is an exact multiple of the chunk size (the final full-sized chunk is still marked
  final).
- [x] Server-side direct-HTTP decryption (`DownloadFileService`) and CLI decryption
  (`CliDownloadSession`) each derive the expected `isFinal` for a chunk from the file's
  total chunk count — never from the response or range span — and supply it when decrypting.
- [x] A clean, uninterrupted CLI download of a multi-chunk file still succeeds and is
  byte-for-byte correct; an interrupted-and-resumed download still succeeds and matches a
  clean download.
- [x] After a CLI download completes, the caller (`DownloadCommandHandler`) verifies the total
  plaintext bytes produced against the manifest `Length`, and — when the manifest carries
  `PlaintextSha256` — verifies the produced plaintext's hash; a mismatch fails the download
  with a clear error. This covers the direct/stdout path, not only the resume/queue path.
- [x] On the file/queue path a verification failure does not move the partial to the final
  output and resets the resume state (the partial and its marker are deleted), so a retry
  re-downloads from scratch instead of resuming the mismatched partial forever; on the
  direct/stdout path verification is detect-only (nonzero exit + stderr message) because
  bytes are already streamed out, and truncation *prevention* there comes from the AAD
  `isFinal` change plus stream-exhaustion checks, not the hash.
- [x] The resume marker file (`*.shadowdrop-partial.json`) is created owner-only, consistent
  with the other sensitive-file writers, so the persisted share token is not world-readable.
- [x] `ShareSecret.FromBytes` requires exactly 32 bytes rather than accepting any buffer of
  at least 32 bytes.
- [x] The dead `DownloadFileService.ResolveAsync(string, ...)` overload is removed.
- [x] `GetEncryptedOffsetForChunkIndex`'s `fullSizedChunkCount` cap carries an assertion or
  comment documenting why an index strictly between `ChunkCount - 1` and `ChunkCount` cannot
  reach it.
- [x] Automated tests need to be written.

## Technical Details

**AAD `isFinal` binding.** Add an `IsFinal` boolean to `ChunkMetadata` and extend
`ChunkEncryptionService`: grow `AadLength` from 34 to 35 and have `BuildAad` write the flag
as a single byte after the existing fields. `EncryptChunk`/`DecryptChunk` need no other
change — the flag flows through the AAD, so an `isFinal` mismatch surfaces as the existing
`CryptographicException` on decrypt, which `DownloadCommandHandler` already maps to
"Decryption failed." Note: files uploaded before this change become undecryptable (AAD
mismatch); wipe or re-upload any existing dev/test data after deploying it. All three
`ChunkMetadata` construction sites must supply the flag:

- `EncryptedFileContent.SerializeToStreamAsync` (upload, encrypt): the streaming loop breaks
  on end-of-stream, so it does not know in advance which chunk is last. Compute the total
  chunk count up front from the known plaintext length (`ceil(length / chunkSize)`; uploads
  of zero-length files are already rejected) and set `IsFinal = chunkIndex == chunkCount - 1`.
- `DownloadFileService.LoadNextChunkAsync` (server, direct-HTTP decrypt): it already tracks
  `_nextChunkIndex` against `_uploadedFile.ChunkCount`; set
  `IsFinal = _nextChunkIndex == _uploadedFile.ChunkCount - 1`.
- `CliDownloadSession.CopyDecryptedPlaintextAsync` (CLI decrypt): it already derives
  `chunkCount` via `GetChunkCount`; set `IsFinal = chunkIndex == chunkCount - 1`. This is
  what makes truncation fail: when a server under-declares the total, the CLI computes
  `isFinal = true` for a chunk the uploader encrypted with `isFinal = false`, so the AAD no
  longer matches.

**Post-download verification (defense-in-depth).** This lives in the **caller**, not in
`CliDownloadSession` — the session receives `TotalPlaintextSize` from the server on the direct
path and never sees `PlaintextSha256`, whereas `DownloadCommandHandler.DownloadToStreamAsync`
and `DownloadToFileAsync` hold the manifest `file.Length` and `file.PlaintextSha256`. The
session already exposes `DurablePlaintextLength` (total plaintext bytes written); the caller
compares it against `file.Length` after `DownloadAsync` completes, and, when
`file.PlaintextSha256` is present, verifies the produced plaintext's hash: for the file/queue
path hash the completed partial before the atomic move; for the direct/stdout path tee the
plaintext through an incremental SHA-256 as it is written, since stdout is not re-readable.

The two paths differ in what a failure can achieve. On the **file/queue** path, throw a
`DownloadCommandException` without moving the partial into place, and reset the resume state
(delete the partial and its marker): a hash-mismatched complete partial has no salvage value,
and if left in place every retry would resume it as already-complete, download nothing, and
fail the same check forever. On the **direct/stdout** path the plaintext has already been
streamed to the consumer by the time a whole-file hash can be computed, so the check is
**detect-only**: report a clear stderr message and a nonzero exit, but it cannot prevent
delivery. Actual truncation *prevention* on stdout comes from the AAD `isFinal` change (a
re-labelled final chunk fails `DecryptChunk` before its plaintext is written) and the existing
stream-exhaustion checks — the hash is an extra, largely redundant safety net there. Keep the
check independent of the AAD change so it also guards the direct path, which today hashes
nothing.

**Resume marker permissions (finding 2).** In `DownloadCommandHandler.PersistResumeMarkerAsync`,
create the marker owner-only rather than with a plain `FileStream`. Reuse the same owner-only
creation approach used by `AtomicFileWriter`'s `ownerOnly` path (a `UnixCreateMode` on the
`FileStreamOptions`/`FileStream` on Unix; the existing helper already encapsulates the
cross-platform behavior). Do not change the marker's contents or the resume-matching logic.

**Correctness notes.** Tighten `ShareSecret.FromBytes` to require `bytes.Length == KeyLength`
(update the guard and its exception message/XML doc); callers already pass exactly 32 bytes.
Remove the unused `DownloadFileService.ResolveAsync(string, ...)` overload (the endpoint
builds a `DownloadRequest` via `TryCreateDownloadRequest`), and update the existing
`DownloadFileServiceTests` call sites to use `ResolveAsync(DownloadRequest, ...)` instead.
Preserve coverage for mode and range request parsing through endpoint/request-construction
tests rather than leaving it coupled to the removed overload. Add an assertion or explanatory
comment at the `fullSizedChunkCount` cap in `GetEncryptedOffsetForChunkIndex`.

**Tests.** No persisted-ciphertext migration is needed: a repo-wide search found no stored
ciphertext fixtures (no binary/embedded-resource fixtures, no long base64/hex literals in
tests), and the E2E smoke tests round-trip live (upload then download) rather than asserting
against precomputed ciphertext. The AAD change alters only the GCM tag *value*, not ciphertext
bytes or chunk lengths (AAD is external authenticated data, never stored; the tag stays 16
bytes), so encrypted-length assertions and the opaque-blob persistence tests are unaffected —
only live encrypt/decrypt round-trips exercise the new AAD and they regenerate automatically.

Extend the crypto tests so an encrypt/decrypt round-trip with mismatched `isFinal` throws
`CryptographicException`, and update the `AadLength` expectation to 35. Add a CLI download test
using a fake HTTP response that serves the leading full-sized chunks of a longer file while
declaring the shorter total, and assert the download fails rather than producing truncated
output. Add a post-download length/SHA-256 mismatch test for the direct path (asserting the
detect-only nonzero-exit behavior) and for the file path (asserting the partial is not moved
into place and the partial and marker are deleted). Add a `DownloadFileServiceTests` direct-HTTP regression for a range whose response
span ends before the uploaded file's actual final chunk; the test should prove `isFinal` is
computed from the file chunk index, not from the response span. Add a test asserting the resume
marker is created with owner-only permissions on Unix. Cover the tightened
`ShareSecret.FromBytes` length guard.
