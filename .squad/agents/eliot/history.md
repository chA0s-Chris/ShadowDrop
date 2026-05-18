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
