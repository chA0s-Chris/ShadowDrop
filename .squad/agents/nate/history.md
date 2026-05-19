# SUMMARY — Generated 2026-05-19T10:20:42.080364Z

**Coverage:** May 14–19, 2026  
**Sessions:** Multiple  
**Key themes:** Issue #27 planning, PR #28 review notes, request validation hardening

### Highlights

- **Issue #27 sequencing:** Moved up after issue #15; CLI v2 contract with streaming binary, Range header mode gating
- **Plan #27 refinements:** Five surgical edits locking decision matrix, legacy parameter retirement, mode defaults, validation determinism  
- **PR #28 review notes:** Two unresolved in download hardening (header sanitization, chunk-length arithmetic); flagged for implementation
- **Test coverage:** API walking skeleton gaps closed (public downloads, bearer tokens, bootstrap failures)
- **Architecture:** Emphasized no `dev` branch; squad/issue-slug branching; NUnit 4 + FluentAssertions, no mocking library

### Related Work

- Parker (Tester): PR #10 review fixes, test coverage expansion
- Tara (Platform): Chunk metadata, download headers, crypto hot paths
- Alec (Security): Header injection prevention, char sanitization
- Eliot (Backend): Direct-HTTP fail-closed setup


---

## Archived Details

# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
   - **Copilot Finding:** `Read(Span<byte>)` override allocates new byte[] on every call then immediately discards, defeating span benefits and creating GC pressure
   - **Local Fix:** Removed the entire override (deleted lines 625-633)
   - **Effect:** Base `Stream` class now handles span calls via pooled buffers—zero per-call allocation
   - **Reply Posted:** Concrete explanation with deletion line range

**Resolution Status:** Both threads automatically marked as `isResolved: true` after replies posted.

**Verification:** GraphQL query confirmed both threads show `isResolved: true`:
- Thread 1 (databaseId 3260101927): `PRRT_kwDOSdMXNc6C4lZr` — resolved ✓
- Thread 2 (databaseId 3260101986): `PRRT_kwDOSdMXNc6C4laW` — resolved ✓

**Outcome:** Both newest review conversations resolved. Parker's approval confirmed local fix set ready. No commits made per instructions. PR #28 prepared for user final review.

- 2026-05-18T21:49:09.624+02:00: PR #28 latest Copilot note is valid and blocking; `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` sanitizes CR/LF only for `X-ShadowDrop-File-Content-Type`, so persisted non-CR/LF control characters still need stripping. Recommended regression coverage belongs in `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`.

- 2026-05-18T22:59:03.328+02:00: Issue #27 planning now assumes ShadowDrop can replace the current CLI JSON/Base64 resumable-download contract instead of preserving v1 compatibility. Preferred transport shape is a streamed binary response with explicit negotiation and deterministic metadata headers; likely implementation touchpoints are `ai-plans/0027-streamed-binary-v2-cli-download-contract.md`, `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`, `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, and `src/ShadowDrop.Cli/Downloads/CliResumableDownloadContractParser.cs`.
- 2026-05-18T23:04:42.962+02:00: Issue #27 transport shape locked. Christian Flessa approved: raw encrypted bytes in response body; metadata in deterministic HTTP headers; custom `application/vnd.shadowdrop.cli-download` content type. Plan `ai-plans/0027-streamed-binary-v2-cli-download-contract.md` updated with concrete header names (`X-ShadowDrop-*`), content-type, and implementation touchpoints. Headers: `First-Chunk-Index`, `Last-Chunk-Index`, `Plaintext-Range-Start`, `Plaintext-Range-End`, `Total-Plaintext-Size`, `Chunk-Size`, `Final-Chunk-Plaintext-Length`. No body preamble/footer; streaming-first design reuses existing crypto and auth gates.
- 2026-05-18T23:10:12.515+02:00: Issue #27 plan refinement: ShadowDrop replaces v1 JSON/Base64 CLI contract with binary streaming—no version suffix in query selector. Christian Flessa decision: use `?mode=cli` (not `?mode=v2`). Rationale: ShadowDrop is still pre-release; this binary contract is the actual v1 on public release. Plan updated to remove version-suffix language and lock negotiation to explicit `?mode=cli` query parameter.
- 2026-05-18T23:11:54.206+02:00: Cleaned up plan 0027 Rationale section to remove residual "v2" framing. Ensured internal consistency: Rationale now clarifies that the binary contract is the authoritative CLI shape on release (not a v2 option), negotiated via `?mode=cli`. Acceptance criteria and technical details already correctly reference mode selector and avoid v2 language.

## 2026-05-18T23:24:50.124+02:00: Issue #27 Plan Finalization — Range Header Locking

**Task:** Update ai-plans/0027-streamed-binary-v2-cli-download-contract.md to lock in the decision that CLI mode uses `?mode=cli` plus standard HTTP `Range: bytes=...` header for subset selection, with legacy query parameters (`plaintextStart`, `plaintextEndExclusive`) retired and unsupported in CLI mode.

**Changes Applied:**

All six subsections of Technical Details updated for internal consistency:

1. **Request / Negotiation Rules:** Locked `Range: bytes=start-end` as the only subset selector for CLI mode, interpreted against plaintext offsets. Explicit rejection of requests mixing `Range` headers with legacy query parameters.

2. **CLI HTTP Semantics:** Specified `200 OK` response code (not 206), no `Accept-Ranges`/`Content-Range` in response (redundant with ShadowDrop headers). Added explicit Range header parsing rules: validate `bytes=start-end` format, reject malformed/overlapped/contradictory ranges with `400`, unsatisfiable ranges with `416` (no file-size leakage).

3. **Wire Integrity Rules:** Added request-side Range header validation before processing, with malformed/overlapped/unsatisfiable rejection. Clarified that Range header must be consistent with plaintext window and body length.

4. **API Implementation:** Specific language around parsing standard `Range: bytes=start-end` header, rejecting legacy query parameter mixing, mapping plaintext range to encrypted chunk span, and locking exact parsing rules in one place.

5. **CLI/Shared Implementation:** Explicit instruction to construct `Range: bytes=start-end` request headers for resume state instead of legacy query parameters.

6. **Testing:** Added specific Range header test scenarios: valid `bytes=start-end` formats, overlapped/malformed range rejection, unsatisfiable ranges with `416` and no leakage, rejection of mixing with legacy parameters.

7. **Acceptance Criteria:** Refined three criteria to explicitly reference `Range: bytes=...` as the request-side mechanism and to call out testing for Range header acceptance/rejection.

**Knock-on Implications:**

- **Legacy Code Removal:** Any CLI-mode code path using `plaintextStart`/`plaintextEndExclusive` query parameters must be removed during implementation; no fallback or dual-path allowed.
- **Range Validation:** Both API and CLI must implement robust `Range: bytes=...` parsing with explicit handling for malformed, overlapped, unsatisfiable, and mixed-parameter cases.
- **Clean Separation:** Mode negotiation (`?mode=cli` query param) is now cleanly separated from plaintext window selection (`Range: bytes=...` header). Removes ambiguity and enables deterministic unit testing.
- **Documentation Binding:** Plan is now the single authoritative source for Range header semantics in CLI mode; no guesswork during implementation.
- **Testing Scope:** Test matrix expanded to cover 6+ Range header edge cases plus 2+ mixing scenarios with legacy query parameters.

**Status:** Plan is now locked, internally consistent, and ready for implementation assignment. No further scope changes expected.

## 2026-05-18 — Plan 0027: Immediate Replacement Decision

**What:** Edited 0027-streamed-binary-v2-cli-download-contract.md to eliminate the coexistence contradiction. Plan now commits to immediate replacement of legacy CLI v1 JSON/Base64 contract.

**Key Changes:**
- Line 74: Omitted `mode` now routes only to direct-HTTP decryption; v1 path retired this slice
- Line 76: Legacy query parameters (`plaintextStart`/`plaintextEndExclusive`) fully retired, rejected on all paths
- Line 111–112: Negotiation matrix updated; omitted mode goes direct-HTTP, legacy params return 400
- Line 31–32: Acceptance criterion now says "removed completely" not "removed or retired"
- Line 170–172: CLI/shared implementation section clarified: removal includes all v1 DTOs, serializers, tests

**Pattern:** ShadowDrop has no active external users; immediate replacement is cleaner than deferred dual-path support. Acceptance criteria now have one story, no fallback branches.

**Files:** ai-plans/0027-streamed-binary-v2-cli-download-contract.md

## 2026-05-19 — Scribe: Issue #27 Follow-up Review Gate Closure

**Agents involved:** Tara, Nate, Parker  
**Context:** PR #28 review cycle closed on issue #27 follow-up work

Tara resolved two findings:
- Rejected explicit empty/whitespace mode selectors
- Repaired bearer-token tests (ResolveAsync signature)
- Added end-to-end API test for empty mode rejection
- Test suite validated (194 tests green)

Decision inbox consolidated (21 files merged to decisions.md).
Archive gate passed; no forced archival. Ready for next phase.
