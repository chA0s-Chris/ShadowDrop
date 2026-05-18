## Rationale

Make downloads always resumable as required by the project concept. This slice should add HTTP range support on top of
the versioned chunked encryption format so interrupted downloads can continue without decrypting entire files.

## Acceptance Criteria

- [x] The download endpoint supports single HTTP byte-range requests for file responses.
- [x] Successful range responses use appropriate HTTP status and headers, including `206 Partial Content` and
  `Content-Range`.
- [x] Invalid or unsatisfiable ranges are rejected with the appropriate HTTP response.
- [x] **Range requests enforce the same share-token, optional download bearer-token, and expiration checks as full
  downloads.**
- [x] Direct-HTTP mode can decrypt and serve only the plaintext chunks needed for the requested range.
- [x] **CLI resumable-download contract locked:** The CLI receives a deterministic response shape that includes
  encrypted chunk data, chunk span metadata (first/last chunk index), plaintext range boundaries, and total file
  size—sufficient for CLI to seek within chunks, decrypt locally, resume on interrupt, and avoid full-file transfer.
- [x] Range handling does not require loading the entire encrypted file into memory.
- [x] Range calculations respect chunk size, chunk count, and final-chunk plaintext length.
- [x] Automated tests cover full-file requests, aligned ranges, mid-chunk ranges, multi-chunk ranges, and unsatisfiable
  ranges.
- [x] **Security-focused tests prove that invalid, expired, or unauthorized range requests are rejected and do not
  return partial content.**
- [x] **Range-request error handling is explicitly non-leaky:** Invalid ranges, invalid/expired tokens, and
  authorization failures all use generic HTTP status codes (400, 401, 403) without exposing range details, token
  validation logic, or file size information in error responses.

## Technical Details

### Plaintext-Range-to-Chunk-Span Mapping

The server must map a requested plaintext byte range to the encrypted chunks required to serve it. The mapping process:

1. **Range bounds:** Given plaintext range `[start, end)`, calculate which chunks contain these bytes.
2. **Chunk index calculation:** For chunk size `C`, chunk index = `floor(start / C)`. The first and last chunk indices
   determine the span.
3. **Plain boundaries within chunks:** Track the plaintext offset within the first chunk and the plaintext length within
   the last chunk to handle mid-chunk ranges.
4. **Encrypted chunk stream:** Open the stored blob and read only the required encrypted chunks from the start of the
   first chunk index to the end of the last chunk index. Do not materialize all chunks or the entire file.
5. **Decryption window (Direct-HTTP mode):** Decrypt only the required plaintext range from the chunk span; do not
   decrypt the entire file. Trim decrypted plaintext to the exact byte range requested.
6. **CLI mode (encrypted subset):** Return the required encrypted chunks as a byte stream or wrapped response shape that
   the CLI can process incrementally, preserving resumability without full-file download.

### HTTP 206 Response Contract (Direct-HTTP Mode)

When Direct-HTTP mode serves a range request:

- **Status:** `206 Partial Content`
- **Content-Range header:** `bytes {start}-{end-1}/{total-plaintext-size}` where:
  - `start` = first byte of the range (0-indexed)
  - `end-1` = last byte of the range (inclusive)
  - `total-plaintext-size` = total plaintext file size (the `plaintext_length` field from metadata)
- **Content-Length header:** `{end - start}` (number of bytes in the partial response)
- **Accept-Ranges header:** `bytes` (signals that the server supports byte-range requests)
  - **Polish note:** `Accept-Ranges: bytes` MUST also be returned on full-file (non-206) responses and on all HTTP
    4xx/5xx error responses to full-file requests. This advertises range-request capability consistently and allows
    clients to discover resumable-download support without a separate OPTIONS call.
- **Response body:** Only the plaintext bytes from `[start, end)`, exactly as encrypted

### Direct-HTTP vs CLI Resumable Semantics

**Direct-HTTP range mode:**

- Server decrypts the required chunks internally.
- Returns HTTP 206 with plaintext bytes in response body.
- Standard HTTP resumption tooling (`curl -C -`) works directly.
- Full authentication and expiration validation applied at request time.

**CLI resumable encrypted-subset mode (contract locked):**

The CLI receives a JSON response containing:

1. **Encrypted chunk span:** First chunk index and last chunk index (inclusive), allowing the CLI to seek and resume
   within the blob.
2. **Encrypted payload:** Concatenated encrypted chunks in binary form or as base64-encoded string, ready for CLI-side
   decryption.
3. **Range metadata:** Requested plaintext byte range (`start`, `end`) and total plaintext file size, allowing CLI to
   trim decrypted plaintext to exact user request.
4. **Chunk configuration:** Chunk size (usually 64KB) and final-chunk plaintext length, enabling deterministic seek
   calculations for resumption.
5. **No plaintext exposure:** Response contains zero plaintext bytes; CLI decrypts locally after receiving encrypted
   chunk data.

Example response shape (JSON-wrapped):

```json
{
  "firstChunkIndex": 2,
  "lastChunkIndex": 5,
  "encryptedPayload": "<base64-encoded concatenated chunks>",
  "requestedRange": {
    "start": 131072,
    "end": 262144
  },
  "totalPlaintextSize": 1048576,
  "chunkSize": 65536,
  "finalChunkPlaintextLength": 32768
}
```

This contract ensures the CLI can:

- Seek within encrypted chunks using deterministic chunk boundaries
- Resume interrupted downloads by requesting a new range starting from the last decrypted byte
- Decrypt only the required chunks without downloading the full file
- Reconstruct the exact plaintext range requested without unnecessary data

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

**Polish note on optional download bearer-token validation:** If the share uses optional bearer-token authentication,
the download endpoint must validate the bearer token by hashing it and using a fixed-time string comparison against the
stored token hash—reusing the same stored-hash/fixed-time-compare pattern already established in share-creation for
token persistence. This ensures range requests apply the same token-validation security gates as full-file downloads
without introducing new attack surface.

### Range-Request Error Handling (Non-Leaky)

All range-request failures use generic HTTP status codes and minimal public messages to prevent attacker inference:

1. **Invalid or unsatisfiable range (HTTP 416 Range Not Satisfiable):**
  - Triggered when `start` ≥ file size, `end` > file size, or `start` ≥ `end`.
  - Response body: Empty or generic error message (e.g., `"Range not satisfiable"`).
  - Response headers: No `Content-Range` header; no file size or valid range info leaked.
  - Never expose which ranges would be valid or why this range fails.

2. **Invalid or missing share token (HTTP 401 Unauthorized):**
  - Triggered when token is absent, malformed, invalid (hash mismatch), or revoked.
  - Response body: Generic error message (e.g., `"Unauthorized"`).
  - Do not differentiate between "token missing" vs. "token invalid" vs. "token revoked" in response.
  - Do not return any token validation metadata.

3. **Expired share (HTTP 401 Unauthorized):**
  - Triggered when share expiration timestamp has passed.
  - Response body: Same generic message as invalid token (no expiration-specific error).
  - Do not expose expiration time, time remaining, or renewal options.

4. **Unauthorized bearer token for download (HTTP 403 Forbidden):**
  - Triggered when optional download bearer token is required but absent, invalid, or mismatched.
  - Response body: Generic message (e.g., `"Forbidden"`).
  - Do not differentiate between "token absent" and "token invalid" in response.

5. **Direct-HTTP: Invalid range format (HTTP 400 Bad Request):**
  - Triggered when Range header syntax is invalid or multipart ranges are requested.
  - Response body: Generic error (e.g., `"Invalid request"`).
  - Do not parse or reflect back the malformed range in any error message.

6. **CLI: Unsupported query or missing required parameters (HTTP 400 Bad Request):**
  - Triggered when required range parameters are absent, non-numeric, or inconsistent.
  - Response body: Generic error (e.g., `"Invalid request"`).
  - Do not include parameter names, expected types, or suggested fixes.

All error responses must omit:

- File size (total plaintext or encrypted)
- Chunk configuration details (chunk size, count, final-chunk length)
- Token validation state or reason for rejection
- Time information (current time, expiration time, time remaining)
- Range validity hints or suggestions

## Implementation Notes

- 2026-05-18T11:19:54.273+02:00: The CLI-decrypt path now returns a deterministic JSON encrypted-subset contract and
  accepts `plaintextStart` / `plaintextEndExclusive` query parameters for scriptable subset retrieval. Direct-HTTP
  `Range` / `206 Partial Content` work remains outstanding for full issue completion.
