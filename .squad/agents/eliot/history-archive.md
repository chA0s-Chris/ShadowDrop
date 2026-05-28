# Eliot — Backend Developer (Archived)

## Pre-Archive Summary

**Total entries archived:** All entries from 2026-05-14 through 2026-05-28
**Key domains:** Range request handling, streamed JSON contracts, header sanitization, Base64 validation, resource cleanup
**Recent focus (pre-archive):** Issue #15 completion (CLI resumable downloads v1), PR #28 follow-ups (header injection, O(1) calculations)

### High-Impact Work

1. **Issue #15 — Streaming CLI Range JSON** (2026-05-18T11:23:46Z)
   - Preserved deterministic JSON contract while streaming encrypted payload (avoids full materialization)
   - Kept DTO on `ContractsJsonSerializerContext` for centralized wire shape
   - Added regression coverage for lazy source reads

2. **PR #28 Review Fixes** (2026-05-18T15:26:37.377+02:00 through 2026-05-18T23:24:50.124+02:00)
   - Header injection prevention: sanitized CR/LF + control characters in custom headers
   - O(1) chunk-span plaintext length calculation (replaced O(n) loop)
   - Zero-allocation Base64 validation (checks format without allocating decoded array)
   - Base64 padding contiguity enforcement (rejects "AB=C" style malformed padding)
   - Empty-file suffix-range bug fix (returns RangeNotSatisfiable instead of invalid [0,0) range)
   - Base64EncodingStream.Read(Span<byte>) allocation elimination

3. **Plan 0027 Impact Analysis** (2026-05-18T23:24:50.124+02:00)
   - Identified 8 key surfaces affected by future `?mode=cli` + `Range: bytes=` negotiation
   - Flagged parameter explosion risk in DownloadFileService overloads
   - Proposed decision table for mode validation (mode, DirectHttpEnabled, rangeHeader, queryParams)

4. **PR #29 Coordination** (2026-05-19T16:52:49Z)
   - Numeric CLI metadata header formatting: must use `CultureInfo.InvariantCulture`
   - CLI parser enforces ASCII digits; culture-sensitive output causes parsing failures

### Key Patterns

- Header sanitization is cross-cutting (API responses → custom headers → CLI parsing)
- Contract lock strategy: v1 compat maintained while preparing v2 migration path
- Streaming + ranges = complex; regression coverage at producer+consumer boundary (API + CLI tests together)
- Zero-allocation validation is critical for public endpoints (prevents OOM from malicious payloads)

### Related Archive

- Full entry history preserved in this section before 2026-05-29 truncation
- Decisions.md also archived separately
