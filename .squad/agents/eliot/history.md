# Eliot — Backend Developer

## Context

- **Role:** Backend Dev for ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Archive:** Detailed learnings from 2026-05-14 to 2026-05-18 AM documented in `history-archive.md`

## Active Work — Issue #15 (2026-05-18)

### Streaming CLI Range JSON Without Buffering

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`, `tests/ShadowDrop.Shared.Tests/Contracts/CliResumableDownloadContractTests.cs`

- The CLI resumable-download response can keep the existing deterministic JSON contract while avoiding full encrypted-span `byte[]` materialization by streaming three segments: serialized JSON prefix, on-the-fly Base64 of the encrypted chunk span, and serialized JSON suffix.
- Keeping the transport DTO on `ContractsJsonSerializerContext` for both API and CLI centralizes the wire shape even when the payload value itself is streamed rather than prebuilt.
- Large-range regression coverage should assert lazy source reads before the payload portion is consumed; that catches accidental reintroduction of eager buffering even when the response stream type changes.

---
date: 2026-05-18T11:23:46.000Z
team-update: true
---

## Cross-Agent: Issue #15 Review Fixes Completion

**Status:** Merged
**Agents:** Eliot (Backend Dev), Parker (Tester), Nate (Lead)

### Team Outcome

Issue #15 review findings addressed across all layers:

1. **Eliot (Backend):** Fixed CLI resumable JSON contract buffering by streaming encrypted payload instead of full materialization. Preserved contract shape via `ContractsJsonSerializerContext`.
2. **Parker (Tester):** Added dual-edge regression coverage (API producer + CLI consumer) to lock v1 contract integrity.
3. **Nate (Lead):** Created issue #25 for future streamed binary v2 contract migration (future work, not blocking #15).

### Decisions Merged

- `decisions.md` now contains:
  - Eliot — CLI Range Fix: Streaming Encrypted Payload (v1 Contract Lock)
  - Parker — CLI Range Fix Regressions: Dual-Edge Coverage
  - Nate — Issue #25 Created: CLI Resumable Downloads v2 Contract Migration

### Related

- Session Log: `2026-05-18T11:23:46.000Z-issue-15-review-fixes.md`
- Orchestration Log: `2026-05-18T11:23:46.000Z-eliot.md`

---
date: 2026-05-18T15:26:37.377+02:00
---

## Learnings — PR #28 Review Fixes

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`, `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `src/ShadowDrop.Cli/Downloads/CliResumableDownloadContractParser.cs`, `ai-plans/0015-range-requests-and-resumable-downloads.md`

### Header Injection Prevention

- User-controlled metadata (filename, content-type) written to custom response headers can enable header injection attacks if not sanitized.
- Implemented `SanitizeHeaderValue` helper that strips CR/LF characters and enforces 500-char length limit before writing to `X-ShadowDrop-FileName` and `X-ShadowDrop-FileContentType` headers.
- Critical security pattern: always sanitize external data before writing to HTTP response headers, even custom headers.

### O(1) Chunk-Span Length Calculation

- Replaced O(n) loop that summed plaintext lengths across chunk range with O(1) calculation.
- Added `GetPlaintextLengthForChunkSpan` helper: computes plaintext length for chunk span by recognizing that all non-final chunks have `chunkSize` plaintext, final chunk has `finalChunkPlaintextLength`.
- Formula: `(chunkCount - 1) * chunkSize + finalChunkPlaintextLength` if range includes final chunk, else `chunkCount * chunkSize`.
- Performance improvement eliminates linear iteration for large-range requests, critical for multi-GB file ranges.

### Zero-Allocation Base64 Validation

- Base64 validation via `Convert.FromBase64String` allocates a full decoded byte array just to check format validity.
- Implemented `IsValidBase64String` that validates Base64 character set and padding rules without allocation.
- Validates: length divisible by 4, character set (A-Z, a-z, 0-9, +, /), padding only in final two positions.
- Prevents OOM when malicious payloads include multi-MB Base64 strings in CLI resumable-download contract.

### Plan Synchronization Discipline

- Updated stale implementation note in plan 0015 to reflect that Direct-HTTP range support is now complete.
- Pattern: always sync plan acceptance criteria and implementation notes when backend contracts change or work completes.

---
date: 2026-05-18T15:55:26.590+02:00
---

## Learnings — PR #28 Follow-Up Fixes

**Files touched:** `src/ShadowDrop.Cli/Downloads/CliResumableDownloadContractParser.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/CliResumableDownloadContractParserTests.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`

### Base64 Padding Contiguity Enforcement

- The initial Base64 validator checked padding position but didn't enforce contiguity: "AB=C" could pass if '=' was at `length - 2`.
- Fixed by tracking `paddingCount` and rejecting any non-padding character after padding starts.
- Comprehensive test coverage now includes: non-contiguous padding ("AB=C"), early padding ("A=BC"), and excessive padding ("A===").
- Critical security pattern: zero-allocation validation must still enforce all Base64 encoding rules, not just character set and divisibility.

### Header Sanitization Test Expectations

- Test previously asserted that custom header preserved the original unsanitized value including CR/LF.
- After implementing `SanitizeHeaderValue`, test must expect the sanitized value (CR/LF stripped) while still verifying fallback behavior.
- Pattern: when adding sanitization to production code, update assertions to verify sanitization occurred, not that original malicious input is preserved.
- Test now asserts: (1) no CR/LF in custom header, (2) other characters preserved, (3) Content-Type still falls back to `application/octet-stream`.

---
date: 2026-05-18T17:36:33.042+02:00
---

## Learnings — PR #28 Final Review Fixes

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`

### Empty-File Suffix-Range Bug Fix

- Suffix ranges (e.g., `Range: bytes=-1`) on zero-length files incorrectly returned `Start=0, End=0` (an empty invalid range) instead of returning `RangeNotSatisfiable` (416).
- Root cause: When `totalPlaintextLength` is 0, `suffixLength = Math.Min(requestedBytes, 0) = 0`, producing `start = 0 - 0 = 0` and `endExclusive = 0` — an invalid [0,0) range.
- Fix: Added explicit check in suffix-range branch: if `suffixLength == 0`, return `RangeNotSatisfiable` immediately before constructing the range.
- Pattern: suffix ranges must be rejected as unsatisfiable when the file is empty, not treated as a zero-length valid range.
- Note: Zero-length uploads are explicitly blocked by upload validation (`MultipartUploadRequestReader`), so this case is only reachable through edge conditions or direct database manipulation. Tested via full API test suite; isolated unit test not added due to upload restriction.

### Base64EncodingStream.Read(Span<byte>) Allocation Elimination

- The `Read(Span<byte>)` override allocated a fresh `byte[]` on every call, defeating the purpose of the span-based API and creating GC pressure.
- Original implementation: allocate `new byte[buffer.Length]`, call `Read(byte[], int, int)`, copy into span.
- Fix: Removed the `Read(Span<byte>)` override entirely, allowing base `Stream` class to provide its own implementation that calls `ReadAsync(Memory<byte>)` directly.
- Base `Stream.Read(Span<byte>)` implementation already delegates to `ReadAsync(Memory<byte>)` correctly without allocations; no need to override.
- Pattern: when implementing async-first stream types, only override `ReadAsync(Memory<byte>)`; remove any allocating span shims and let base class handle synchronous/span paths.
- Verified via full API, CLI, and Shared test suites (168 tests total).

---
date: 2026-05-18T21:54:45.116+02:00
---

## Learnings — PR #28 Header Control Character Follow-Up

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`

### Custom Header Sanitization Boundary

- `GetResponseContentType` already protects the framework-managed `Content-Type` header, but mirrored metadata written to `X-ShadowDrop-File-Content-Type` needs its own sanitization path.
- For persisted metadata echoed into headers, stripping only CR/LF is not sufficient; remove every control character (`Char.IsControl`) before truncating to the existing 500-character cap.
- The safest regression lives at the API level: persist a control-character-tainted content type in metadata storage, download the file, and assert both successful fallback (`application/octet-stream`) and sanitized custom-header output.

---
date: 2026-05-18T22:35:59.710+02:00
---

## Learnings — PR #28 Direct-HTTP Fail-Closed Setup

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`

### Direct-HTTP Setup Exception Mapping

- Direct-HTTP download setup must treat both `InvalidDataException` (corrupt metadata) and `IOException` (initial skip/read failure) as the same non-leaky invalid-request result already used by the CLI-decrypt path.
- This mapping belongs in `TryOpenDirectHttpContentAsync()` because `DirectHttpDecryptingStream.CreateAsync()` can fail before any response is returned but after the encrypted blob stream is opened.
- Regression coverage should assert the blob stream still gets disposed when direct-HTTP setup fails closed, so the error handling path does not leak file handles while hiding the root cause from callers.

---
date: 2026-05-18T23:24:50.124+02:00
---

## Findings — Plan 0027 Impact Analysis

**Task:** Identify backend/API areas affected by `?mode=cli` + `Range: bytes=...` negotiation for streamed binary v2 CLI contract.

### Key Affected Surfaces

1. **DownloadEndpoints.cs** (lines 33–34, 75–83)
   - Must extract new `?mode=cli` query parameter to route negotiation
   - TryGetInt64QueryValue method becomes deprecated for plaintextStart/End in CLI mode

2. **DownloadFileService.ResolveAsync() overloads** (lines 50–76, all three)
   - Currently three overload signatures (5-param, 7-param, 9-param)
   - Must thread mode parameter through all chains to ResolveRequestedRange
   - **Risk:** Parameter explosion; consider request object bundling

3. **ResolveRequestedRange()** (lines 391–440)
   - Current check rejects mixing Range header + query params (line 398–401)
   - Must implement three-way logic:
     - `mode=cli`: reject plaintextStart/End entirely; accept only Range header
     - Omitted mode: preserve v1 behavior (backward compat)
     - Unknown mode: reject as invalid
   - **Risk:** Complex branching; decision table must be explicit and locked

4. **Mode selection at line 127** (`share.DirectHttpEnabled ? DirectHttp : CliDecrypt`)
   - New validation matrix needed:
     - `?mode=cli` + DirectHttpEnabled → reject (key material model conflict)
     - `?mode=cli` + CliDecrypt → binary contract path (new)
     - Omitted mode + CliDecrypt → JSON contract path (v1 compat)

5. **CLI response producer** (TryOpenCliDecryptContentAsync lines 470–515, CreateCliDecryptJsonStreamAsync lines 207–243)
   - Must branch on mode:
     - `mode=cli`: raw encrypted bytes + X-ShadowDrop-* metadata headers (firstChunkIndex, lastChunkIndex, plaintextRange, totalSize, chunkSize, finalChunkLength)
     - Omitted mode: keep JSON contract (CliResumableDownloadContract + Base64 payload)
   - **Risk:** Two parallel content streams; maintenance burden

6. **Response header output** (DownloadStreamResult.ExecuteAsync lines 139–156)
   - ContentType negotiation now three-way (DirectHttp, CliDecrypt-JSON, CliDecrypt-Binary)
   - Mode=cli responses must use `application/vnd.shadowdrop.cli-download`
   - Metadata headers for binary mode must be deterministic and present

7. **Test coverage** (ApiWalkingSkeletonTests line 620, DownloadFileServiceTests methods)
   - Current test `?plaintextStart=64&plaintextEndExclusive=120` must remain for v1 compat
   - New tests required:
     - `?mode=cli + Range: bytes=` → 206 binary response
     - `?mode=cli + plaintextStart/End` → 400 rejection
     - Omitted mode + plaintextStart/End → 206 JSON response (v1)
     - `?mode=cli` on DirectHttp share → 400 or 403

8. **DownloadFileResolution model** (communication between service and endpoint)
   - Currently carries ResponseContentType, RequestedRange for Content-Range header
   - May need mode-specific metadata fields or separate resolution model for binary path

### Implications for Plan Update

- Explicit decision table needed: (mode, share.DirectHttpEnabled, rangeHeader, queryParams) → (action, statusCode, responseFormat)
- Clarify if plaintextStart/End are only invalid when `mode=cli`, or deprecated entirely for CLI
- Lock the exact point where mode validation occurs (endpoint vs service vs helper)
- Decide if DownloadFileService should bundle (mode, rangeHeader, plaintextStart/End) into request object to reduce overload sprawl

## Session: Team Integration (2026-05-19T13:14:01Z)
Both Eliot and Sophie completed targeted fixes in download service handling.
- Eliot: DownloadFileService range header validation (23 tests passing)
- Sophie: LengthValidatingReadStream disposal fix (CliDownloadResponseParserTests validated)
Ready for integration.

## 2026-05-19T16:52:49Z: PR #29 Fix Assignment — Numeric Header Invariant Culture

**From:** Scribe (post-Nate review assessment)
**Status:** Awaiting coordinator spawn

**Assignment:** Fix numeric CLI metadata header formatting in `DownloadEndpoints.cs`
- Issue: Plain `ToString()` for chunk indices, ranges, sizes can produce culture-sensitive output
- CLI parser enforces ASCII digits only, so culture-emitted headers fail parsing
- Fix: Format every numeric header with `CultureInfo.InvariantCulture`
- Add API/CLI regression tests proving canonical wire values parse correctly

**Files to touch:**
- `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` (formatting)
- `tests/ShadowDrop.Api.Tests/...` or `tests/ShadowDrop.Cli.Tests/...` (regression)

**Coordinate with:** Parker (for complementary final-chunk check tests)

---
date: 2026-05-23T23:28:14.726+02:00
---

## Learnings — PR #30 CLI Upload Review Fixes

**Files touched:** `src/ShadowDrop.Cli/Configuration/CliConfigurationResolver.cs`,
`src/ShadowDrop.Cli/Uploads/EncryptedFileContent.cs`, `src/ShadowDrop.Cli/Uploads/UploadApiClient.cs`,
`src/ShadowDrop.Cli/Uploads/UploadCommandHandler.cs`, `src/ShadowDrop.Cli/Uploads/UploadMetadataPayload.cs`,
`src/ShadowDrop.Shared/Crypto/EncryptedChunk.cs`, `src/ShadowDrop.Shared/Properties/AssemblyInfo.cs`,
`tests/ShadowDrop.Cli.Tests/Uploads/EncryptedFileContentTests.cs`,
`tests/ShadowDrop.Cli.Tests/Uploads/UploadCommandHandlerTests.cs`

### CLI Upload Streaming Cancellation

- `HttpContent` upload streams need the request cancellation token threaded into the content instance and honored inside
  both file reads and response-stream writes; otherwise CLI cancellation can stall until the current chunk finishes.
- For chunked crypto payloads, the cancellation-aware override belongs on
  `SerializeToStreamAsync(Stream, TransportContext?, CancellationToken)` and the request factory must pass the same
  token when constructing the content.

### Zero-Copy Internal Ciphertext Access

- Shared crypto DTOs can keep their public defensive-copy contract while still serving streaming callers efficiently by
  exposing an internal `ReadOnlyMemory<byte>` view for async I/O.
- In this slice, `EncryptedChunk` now keeps `Ciphertext` as the safe public copy-returning API and adds internal
  `CiphertextMemory` so CLI upload streaming stops cloning each ciphertext chunk before `Stream.WriteAsync`.
