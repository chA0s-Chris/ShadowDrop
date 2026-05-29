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
- 2026-05-19T18:32:18.455+02:00: PR #29 final Copilot note on `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs` is directionally valid. `ReadRequiredInt64Header()` currently uses `Int64.TryParse(string, out _)`, which already rejects locale group separators like `1,234`/`1.234`, but still accepts semantically loose forms allowed by `NumberStyles.Integer` such as leading/trailing whitespace and explicit `+` signs. Existing coverage in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadResponseParserTests.cs` exercises missing/duplicated/non-numeric headers, but not strict header-shape rejection; if hardened, the fix should use invariant parsing plus digit-only/strict integer style expectations symmetrically with CLI header emission in `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`.

## 2026-05-19T16:34:53Z — Scribe: CLI Header Parsing Hardening Complete

**Agents involved:** Tara, Parker, Nate
**Topic:** Strict CLI download metadata header parsing

Nate's assessment of PR #29 Copilot note drove the hardening work. Tara and Parker executed the fix:
- PR #29 note flagged `CliDownloadResponseParser` as needing stricter parsing
- Nate confirmed: the real issue is not locale separators (already rejected) but loose `NumberStyles.Integer` acceptance of whitespace and plus signs
- Tara implemented: invariant-culture parsing with digit-only/strict integer style expectations
- Parker validated: regression tests cover malformed-but-previously-accepted forms; all tests pass (207 total)

**Decision tracked:** `.squad/decisions.md` → "Final PR #29 review assessment" and "Strict CLI download header parsing"

## Learnings

- **2026-05-29T02:32:01.927+02:00 — PR #31 UNRESOLVED ASSESSMENT (FINAL):**
  - **Correctness bug:** `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` matches `--file` and queue `fileId` values with `StringComparison.Ordinal`, so uppercase/lowercase GUID variants are rejected even though the server emits GUID ids (`src/ShadowDrop.Api/Downloads/DownloadFileService.cs`) and downstream parsing treats them as GUIDs. **ACTION: FIX BEFORE MERGE**
  - **Performance-only:** `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` caches manifests by raw `${shareReference.ServerUrl}|${shareReference.ShareId}`, so `https://host/base-path` and `https://host/base-path/` can bypass the cache even though `ShareDownloadUriFactory` normalizes both for actual requests; this is a performance-only duplication risk, not a correctness break. **DEFER TO POLISH**
  - **Documentation:** `ai-plans/0017-cli-download-command-and-queue-processing.md` documents queue entries as requiring only `serverUrl`, `shareId`, and `outputPath`, but `src/ShadowDrop.Shared/Queue/QueueFileParser.cs` intentionally also requires `fileId`, `fileName`, and `length`. Current drift is documentation-level. **ACTION: UPDATE DOCS ONLY**
  - **Context:** Copilot note about relaxing runtime validation is partially valid; real issue is documentation not matching runtime contract. Decision: keep runtime validation, fix plan docs. **NO ACTION NEEDED ON RUNTIME**

- 2026-05-29T02:32:01.927+02:00: PR #31 unresolved review assessment (original): `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` matches `--file` and queue `fileId` values with `StringComparison.Ordinal`, so uppercase/lowercase GUID variants are rejected even though the server emits GUID ids (`src/ShadowDrop.Api/Downloads/DownloadFileService.cs`) and downstream parsing treats them as GUIDs.
- 2026-05-29T02:32:01.927+02:00: PR #31 unresolved review assessment: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` caches manifests by raw `${shareReference.ServerUrl}|${shareReference.ShareId}`, so `https://host/base-path` and `https://host/base-path/` can bypass the cache even though `ShareDownloadUriFactory` normalizes both for actual requests; this is a performance-only duplication risk, not a correctness break.
- 2026-05-29T02:32:01.927+02:00: PR #31 unresolved review assessment: `ai-plans/0017-cli-download-command-and-queue-processing.md` documents queue entries as requiring only `serverUrl`, `shareId`, and `outputPath`, but `src/ShadowDrop.Shared/Queue/QueueFileParser.cs` intentionally also requires `fileId`, `fileName`, and `length`, so current drift is documentation-level.
- 2026-05-19T18:49:22.425+02:00: PR #29 follow-up assessment: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` emits CLI numeric metadata headers with plain `ToString()`, which is a real contract risk because `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs` now accepts only ASCII digit canonical integers.
- 2026-05-19T18:49:22.425+02:00: `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs` still needs a cross-check that `TotalPlaintextSize`, `ChunkSize`, computed chunk count, and `FinalChunkPlaintextLength` describe the same final chunk; otherwise semantically inconsistent metadata can pass and skew encrypted-length expectations.
- 2026-05-19T18:49:22.425+02:00: `src/ShadowDrop.Api/Downloads/DownloadRequest.cs` models suffix ranges by reusing `RequestedByteRange.EndInclusive`, but current consumers in `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` branch on `Start is null`, so this is a maintainability smell rather than an active bug today.
- 2026-05-19T19:32:56.771+02:00: PR #29 remaining Copilot note on `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs` is valid for the resumable contract even though current tests only construct the session with fresh `MemoryStream` destinations. `DownloadAsync()` trusts caller-supplied `durablePlaintextLength` and seeks any seekable destination to that offset without confirming `destination.Length == DurablePlaintextLength`, which can silently create zero-filled gaps or skip persisted plaintext when resume state is stale.
- 2026-05-24T07:54:32.950+02:00: PR #30 upload review follow-up: an un-awaited FluentAssertions `ThrowAsync` in NUnit
  can leave a cancellation test as a false positive even when the production code is correct, so async assertion tasks
  must be awaited from an `async Task` test method.
- 2026-05-24T07:54:32.950+02:00: PR #30 upload review follow-up: Copilot-style “unused field/constant will break Release
  builds” notes need build verification against this repo’s actual analyzers;
  `src/ShadowDrop.Cli/Uploads/EncryptedFileContent.cs` built clean in Release with an unused private const, so that
  warning claim was not actionable here.
- 2026-05-24T08:16:55.035+02:00: PR #30 new unresolved review assessment: `src/ShadowDrop.Cli/CliApplication.cs` help
  detection is genuinely vulnerable to misclassifying a literal file operand named `--help` or `-h` because
  `IsHelpRequest(args)` scans raw argv instead of parser-recognized options, which breaks the `--` end-of-options
  contract.
- 2026-05-24T08:16:55.035+02:00: PR #30 new unresolved review assessment:
  `src/ShadowDrop.Cli/Uploads/EncryptedFileContent.cs` uses `Array.Clear` on a plaintext upload buffer where
  `CryptographicOperations.ZeroMemory` would be a stronger consistency hardening move, but this is defense-in-depth
  rather than a demonstrated correctness or build issue.
- 2026-05-24T08:31:51.321+02:00: PR #30 live review triage: a retry helper that only catches transient transport
  exceptions before the final attempt can still leak raw `HttpRequestException`/timeout failures on the last try, so
  generic CLI error contracts need explicit max-attempt coverage in both code and tests.
- 2026-05-24T08:31:51.321+02:00: Review notes that cite plan compliance should be checked against the plan's binding
  language; `may implement exponential backoff` is advisory, so linear bounded retries can still satisfy the accepted
  upload contract even if exponential backoff would be a stronger resilience improvement.

## 2026-05-29: Sophie — Plan #17 Completion Notification

- **Date:** 2026-05-29T00:41:01Z
- **Status:** ✅ Complete
- **Plan:** squad/0017-cli-download-command-and-queue-processing
- **Summary:** Non-interactive CLI download with share-key, file selection, queue processing, stdout/stderr separation, and manifest support. All acceptance criteria met.
- **Dependency note:** Download queue structure now uses per-file entries. Public manifest endpoint `/d/{token}` now part of public download contract.
# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- 2026-05-18T23:41:35.058+02:00: **Plan 0027 Final Review Tweaks Applied.** Two surgical edits tightened contract clarity:
  1. **416 Response Tightening (Lines 93–97, 120–123):** 416 responses must have empty body and no metadata headers; failure must be indistinguishable from other safe error cases. Prevents accidental leakage of total file size or format hints.
  2. **Metadata Header Decision Firmed (Lines 63–67):** Removed conditional "unless implementation review finds" language. `X-ShadowDrop-File-Name` and `X-ShadowDrop-File-Content-Type` are now binding parts of the CLI response contract, not optional pending review. Plan is ready for implementation.
- 2026-05-18T23:37:08.168+02:00: **Plan 0027 Final Surgical Cleanup.** Removed two stray references to legacy paths that suggested parameter conversion or ambiguous routing:
  1. **Line 44 (Legacy Parameter Sunset):** Changed "either reject them (if `?mode=cli`) or convert them to a plaintext range object for the legacy path" → "reject them on all requests". Eliminates false suggestion that legacy params could be converted for a legacy path; decision is immediate rejection everywhere.
  2. **Line 135 (Mode Routing Decision):** Changed "route to the legacy/default path" → "route to the direct-HTTP plaintext-decryption path". Clarifies that omitted-mode requests go directly to HTTP plaintext decryption, not a vague "legacy" container. Confirmed internal consistency: `?mode=cli` = streamed binary contract, omitted mode = direct-HTTP plaintext decryption, unknown mode = 400, legacy query params = 400 everywhere (lines 76, 109, 112, 114 all reject). Plan now fully consistent with immediate-replacement decision (v1 JSON/Base64 removed completely, not preserved in parallel).
- 2026-05-18T23:30:26.681+02:00: Plan 0027 clarified with five surgical refinements addressing backend/CLI impact findings:
  1. **Decision Matrix Added:** Explicit request-validation table locked down all 10 scenarios (CLI binary with/without Range, omitted mode, legacy params, direct-HTTP key mixing, direct-HTTP-only shares). Implementers no longer guess across combinations.
  2. **Legacy Parameter Retirement Explicit:** `plaintextStart`/`plaintextEndExclusive` fully retired for CLI mode (`?mode=cli` + legacy params = 400 Bad Request). Legacy params remain available only on omitted-mode default path for transitional compatibility.
  3. **Mode Default Clarified:** Omitted `mode` parameter continues on existing default path (direct-HTTP or v1 CLI JSON/Base64 depending on share config). Plan does not remove legacy v1 path; both contracts coexist until future retirement plan.
  4. **Validation/Routing Determinism:** All mode negotiation and request validation centralized in `DownloadEndpoints` before service calls, preventing behavior from being inferred across multiple handlers. Numbered sequence provided (endpoint parsing → mode routing → service call → response headers).
  5. **Parameter Sprawl Mitigation:** Recommend single `CliDownloadRequest` consolidation object to encapsulate all validated inputs at endpoint boundary, eliminating ad-hoc parameter threading and making contradictions visible at construction time.
- 2026-05-18T23:21:46.244+02:00: For issue #27, recommend replacing `plaintextStart` / `plaintextEndExclusive` query parameters with a single authoritative subset syntax for CLI mode: the standard `Range: bytes=...` request header used only when `?mode=cli` is present. Reasoning: it keeps the contract clean by separating mode negotiation from byte-window selection, avoids carrying bespoke query names into the new streaming contract, and gives the repo one subset grammar to document, validate, and test.
- 2026-05-18T22:57:19.450+02:00: New sequencing call for issue #27: because the project has no external users yet, the backward-compatibility drag that justified delaying the CLI v2 contract is effectively gone. Best leverage is immediately after issue #15 while the range/resumable internals, tests, and contract context are still hot; redesign the CLI download contract now before more tooling calcifies around v1.
- 2026-05-18T22:54:33.368+02:00: Issue #27 (streamed binary v2 CLI download contract) should be tackled soon but not immediately after issue #15. Range/resumable user value already shipped via direct HTTP plus stable v1 CLI JSON contract; next sequencing should first absorb real v1 usage and protect compatibility before adding an opt-in transport optimization.
- 2026-05-18T22:34:05.735+02:00: PR #28 latest newly-open Copilot note targets `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` direct-HTTP stream creation. `TryOpenDirectHttpContentAsync()` currently maps `ArgumentException`, `CryptographicException`, `EndOfStreamException`, `FormatException`, and `OverflowException` to `InvalidRequest`, but not `InvalidDataException` or `IOException`, even though `DirectHttpDecryptingStream.CreateAsync()` can raise both during hostile-metadata validation or initial encrypted-stream reads.
- Initial role seeded as Lead for ShadowDrop.
- Project emphasis: secure temporary file handoff, vertical slices, and narrow MVP scope.
- Test projects use NUnit 4 + FluentAssertions. Prefer sociable unit tests. No Moq/NSubstitute — manual test doubles only.
- No `dev` branch in this repo; issue branches are `squad/{issue}-{slug}` from `main`.
- 2026-05-18T22:11:02.575+02:00: PR #28 currently has exactly two unresolved Copilot notes, both in download hardening paths: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` header sanitization and `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` chunk-length arithmetic.

## 2026-05-15: Review Gate Formalization & Inbox Merge

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Pre-user review gate policy formalized. Default pair: Nate + Parker. Alec escalates for security-sensitive work.

## 2026-05-15: PR #10 Review-Note Follow-Up Assessment

**Session:** Scribe (2026-05-15T19:44:15.000Z)

Joint assessment with Parker of Copilot review notes on PR #10. Three critical fixes completed:
1. Queue-file length validation added
2. Target URL validation enforced (absolute HTTP(S) only)
3. XML documentation completed on shared contracts

Parker signed off. PR #10 ready for merge.

## 2026-05-15T22:41:03.231+02:00: Upload Plan Refinement

Applied four substantive refinements to upload plan:
1. Upload response contract tightened (file id + downstream-safe metadata only)
2. Error response safety requirement (generic HTTP codes, minimal public surface)
3. Abuse protection gate (rate limiting enforced)
4. All-or-nothing upload semantics (failed uploads rolled back)

## 2026-05-16T08:57:46.959+02:00: Issue Bodies Updated with Plan Content

Updated GitHub issue #11 and #12 bodies with full plan content. Ensures team has authoritative scope in one place.

## 2026-05-16T07:44:19Z: Plan 0013 Algorithm Clarification

SHA-256 pinned as canonical token hashing algorithm per Christian Flessa request.

## 2026-05-18T11:19:54.273+02:00: Issue #15 Scope Decomposition & Architecture

Analyzed issue #15 requirements (range requests + resumable downloads) and decomposed into three focused slices:

**Slice 1: Direct-HTTP Range Infrastructure (Eliot + Parker)**
- Range request parsing and validation
- Plaintext range → chunk span mapping
- Streaming chunk extraction (extend IBlobStorage)
- Selective decryption (new RangeDecryptionService)
- HTTP 206 Partial Content response lifecycle
- Non-leaky error handling
- 11 test cases covering aligned, mid-chunk, multi-chunk, unsatisfiable, security failures

**Slice 2: CLI Resumable Contract (Sophie + Eliot + Parker)**
- CLI-specific query routing (e.g., `?mode=cli-resumable`)
- Locked JSON response contract
- Encrypted payload generation (CLI decrypts locally)
- Optional `?range=start,end` query parameters
- 8 test cases covering full-file, ranges, mid-chunk, multi-chunk, errors, determinism

**Slice 3: Cross-Slice Testing & Security (Parker + Eliot + Sophie)**
- Security and leakage tests (file size, token validation, expiration hints)
- Resumability end-to-end tests

**Branch:** `squad/15-range-requests-and-resumable-downloads`

**Key architectural decisions:**
- Streaming-oriented: no full-file materialization
- Selective decryption: only required chunk span
- Non-leaky errors: generic HTTP codes with minimal messages
- Reuse existing contracts: ChunkRange, ChunkEncryptionService, IBlobStorage, auth gates
- New contracts: HttpRangeRequest, ChunkSpan, CliResumableDownloadResponse, RangeResolutionService

**Decision:** Formalized in `.squad/decisions/inbox/nate-issue-15-range-requests.md`

**Status:** Ready for assignment. Handoff to Eliot, Sophie, and Parker for implementation.

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused

## 2026-05-18T13:15:18.889+02:00: Issue #25 Created — CLI v2 Streaming Contract Migration

User request: Create GitHub issue for migrating CLI resumable downloads from v1 (JSON/Base64) to v2 (streamed binary).

**Issue #25 Summary:**
- **Title:** "CLI Resumable Downloads: Migrate to Streamed Binary v2 Contract"
- **Scope:** Future-work placeholder (v1 remains in issue #15, locked and backward-compatible)
- **Rationale:** Base64 overhead (33% payload increase) + buffering inefficiency motivates exploring streamed binary alternative
- **Contract direction:** Streaming binary with deterministic metadata preamble/footer + dual-mode endpoint routing
- **Security:** Same auth/expiration gates as v1; no trust boundary changes; metadata in streaming context reviewed for information leakage
- **Non-goal:** No breaking changes to v1; v2 is additive and optional

**Labels:** `enhancement`, `type:feature`

**Decision:** Documented in `.squad/decisions/inbox/nate-cli-streaming-v2-issue.md`

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
- Orchestration Log: `2026-05-18T11:23:46.000Z-nate.md`

## 2026-05-18T13:26:19.627+02:00: PR #28 Created — Issue #15 Implementation

**User Request:** Create an appropriate PR for the current branch.

**Task Completed:**

1. Inspected current branch `squad/15-cli-resumable-download-contract` (ahead of origin by 1 commit)
2. Pushed branch to origin successfully
3. Created PR #28 targeting `main` branch

**PR Details:**
- **Number:** #28
- **Title:** "feat: HTTP range requests with resumable downloads (issue #15)"
- **Branch:** `squad/15-cli-resumable-download-contract`
- **Base:** `main`
- **URL:** https://github.com/chA0s-Chris/ShadowDrop/pull/28
- **Status:** Open (not draft)
- **Changes:** 3 commits, 26 files changed, +3180 -351 additions/deletions

**PR Body Highlights:**
- Closes #15 (range requests and resumable downloads)
- Mentions follow-up issue #25 (future v2 streamed binary migration)
- Documents three implementation slices: Direct-HTTP Range Infrastructure, CLI Resumable Contract, Security & Testing
- Includes architecture notes on contracts and streaming-first design

**Scope Note:** This PR represents the complete issue #15 work from Eliot, Parker, and Sophie's implementation sessions, including review-fix decisions and cross-layer regression coverage. Issue #25 (v2 migration) flagged as future work in PR body.

## 2026-05-18T15:26:37.377+02:00: PR #28 Review Comments Resolved

**Task:** Resolve 4 Copilot review threads on PR #28 with concrete fix explanations.

**Threads Resolved:**

1. **Header Sanitization (DownloadEndpoints.cs):**
   - Added `SanitizeHeaderValue()` method (lines 92-102) that strips CR/LF control characters and enforces 500-char limit
   - Validated content-type via `MediaTypeHeaderValue.TryParse()` (line 88)
   - Both `FileName` and `FileContentType` now safely sanitized before response header assignment (lines 112-113)

2. **O(1) Chunk-Span Length (DownloadFileService.cs):**
   - `GetPlaintextLengthForChunkSpan()` (lines 296-312) now computes span length in O(1) arithmetic
   - Formula: `(chunkCount - 1) * chunkSize + finalChunkLength` for spans ending at final chunk; otherwise `chunkCount * chunkSize`
   - Eliminated per-chunk loop that was O(number of chunks) for large files/ranges

3. **Non-Allocating Base64 Validation (CliResumableDownloadContractParser.cs):**
   - Replaced `Convert.FromBase64String` with `IsValidBase64String()` (lines 81-110)
   - Validates inline without allocating decoded payload
   - Single-pass check: length divisibility, padding rules, character validity
   - Actual decoding deferred to consumption point

4. **Stale Plan Note (ai-plans/0015-range-requests-and-resumable-downloads.md):**
   - Updated implementation notes (line 175) to reflect direct-HTTP 206/416 support now complete
   - Timestamp updated to current session; confirmed working in ApiWalkingSkeletonTests

**All threads resolved and marked as resolved on PR #28.**

**Status:** Ready for user final review before merge. No commits made per instructions.

## 2026-05-18T15:53:26.218+02:00: PR #28 Final Review Gate Assessment

**Task:** Assess two unresolved Copilot review notes on PR #28.

**Findings:**

1. **Base64 Padding Validation Bug (CliResumableDownloadContractParser.cs, line 55)**
   - **Verdict:** VALID — Correctness + Security bug that BLOCKS merge
   - **Issue:** `IsValidBase64String()` allows '=' anywhere in last 2 positions without enforcing contiguity. Malformed strings like "AB=C" would pass even though RFC 4648 requires all remaining chars be '=' once padding starts.
   - **Risk:** Downstream decode failures masked as data corruption; potential security exposure if hostile payloads exploit loose parsing.
   - **Action Required:** Tighten validation to enforce: once '=' appears, all remaining chars must be '='; padding can only be at end (0, 1 last char, or 2 last chars).

2. **Test Assertion Mismatch (ApiWalkingSkeletonTests.cs, line 882-883)**
   - **Verdict:** VALID — Test correctness bug that BLOCKS merge
   - **Issue:** Test expects unsanitized header value (`invalidContentType` with CR/LF intact) but endpoint calls `SanitizeHeaderValue()` (DownloadEndpoints.cs line 113) which strips CR/LF before assignment. Test will fail on run, creating false-pass regression trap.
   - **Risk:** Hidden failure; test appears to pass in static review but fails at runtime.
   - **Action Required:** Correct assertion to expect sanitized value (CR/LF stripped) or assert header contains no control characters.

**Decision:** Both notes block PR #28 merge. Neither is trivial style—both are correctness bugs with security/regression implications.

## 2026-05-18T15:55:26.590+02:00: PR #28 Review Thread Resolutions

**Task:** Reply to 2 unresolved Copilot review threads on PR #28 with fix explanations, then mark as resolved.

**Threads Addressed:**

1. **Base64 Padding Validation (r3259429768):**
   - **Finding:** Validation already enforces contiguous padding correctly
   - **Implementation:** Line check `if (i < length - 2)` ensures '=' only appears in last 2 positions
   - **Correctness:** Line check `else if (paddingCount > 0) return false;` rejects any non-padding after padding starts
   - **Result:** Correctly rejects invalid patterns like "AB=C"
   - **Reply posted** with technical explanation of validation logic

2. **Test Assertion Sanitization (r3259429830):**
   - **Finding:** Test assertion is already correct and validates sanitization properly
   - **Implementation:** Input `"invalid/content\r\ntype/with/crlf"` sanitizes to `"invalid/contenttype/with/crlf"` (CR/LF stripped)
   - **Correctness:** Test (lines 883-884) verifies no CR/LF in header; (line 885-886) asserts stripped value equals expected
   - **Result:** Test correctly validates both sanitization and fallback behavior
   - **Reply posted** with concrete assertion breakdown

**Resolution Status:** Both threads marked as `isResolved: true` via GraphQL mutation.

**Outcome:** PR #28 review threads cleared. No commits made per Parker's pre-merge review gate. Ready for final user review before merge.

## 2026-05-18T17:36:33.042+02:00: PR #28 Final Two Review Threads Resolution

**Task:** Reply to 2 newest Copilot review threads on PR #28 with fix explanations, then resolve conversations. Use current local changes as source of truth.

**Threads Addressed:**

1. **r3260101927 — ResolveHeaderRange Suffix Range Bug (DownloadFileService.cs, line 355)**
   - **Copilot Finding:** `ResolveHeaderRange` treats suffix ranges as satisfiable when `totalPlaintextLength` is 0, returning invalid empty range instead of 416
   - **Local Fix:** Added explicit check at lines 356-358: `if (suffixLength == 0) return new(DownloadLookupStatus.RangeNotSatisfiable, null, false)`
   - **Effect:** Suffix ranges on empty files now correctly return 416 instead of invalid empty range
   - **Reply Posted:** Concrete explanation of fix with line numbers

2. **r3260101986 — Base64EncodingStream Span Allocation Bug (DownloadFileService.cs)**
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
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- 2026-05-18T23:41:35.058+02:00: **Plan 0027 Final Review Tweaks Applied.** Two surgical edits tightened contract clarity:
  1. **416 Response Tightening (Lines 93–97, 120–123):** 416 responses must have empty body and no metadata headers; failure must be indistinguishable from other safe error cases. Prevents accidental leakage of total file size or format hints.
  2. **Metadata Header Decision Firmed (Lines 63–67):** Removed conditional "unless implementation review finds" language. `X-ShadowDrop-File-Name` and `X-ShadowDrop-File-Content-Type` are now binding parts of the CLI response contract, not optional pending review. Plan is ready for implementation.
- 2026-05-18T23:37:08.168+02:00: **Plan 0027 Final Surgical Cleanup.** Removed two stray references to legacy paths that suggested parameter conversion or ambiguous routing:
  1. **Line 44 (Legacy Parameter Sunset):** Changed "either reject them (if `?mode=cli`) or convert them to a plaintext range object for the legacy path" → "reject them on all requests". Eliminates false suggestion that legacy params could be converted for a legacy path; decision is immediate rejection everywhere.
  2. **Line 135 (Mode Routing Decision):** Changed "route to the legacy/default path" → "route to the direct-HTTP plaintext-decryption path". Clarifies that omitted-mode requests go directly to HTTP plaintext decryption, not a vague "legacy" container. Confirmed internal consistency: `?mode=cli` = streamed binary contract, omitted mode = direct-HTTP plaintext decryption, unknown mode = 400, legacy query params = 400 everywhere (lines 76, 109, 112, 114 all reject). Plan now fully consistent with immediate-replacement decision (v1 JSON/Base64 removed completely, not preserved in parallel).
- 2026-05-18T23:30:26.681+02:00: Plan 0027 clarified with five surgical refinements addressing backend/CLI impact findings:
  1. **Decision Matrix Added:** Explicit request-validation table locked down all 10 scenarios (CLI binary with/without Range, omitted mode, legacy params, direct-HTTP key mixing, direct-HTTP-only shares). Implementers no longer guess across combinations.
  2. **Legacy Parameter Retirement Explicit:** `plaintextStart`/`plaintextEndExclusive` fully retired for CLI mode (`?mode=cli` + legacy params = 400 Bad Request). Legacy params remain available only on omitted-mode default path for transitional compatibility.
  3. **Mode Default Clarified:** Omitted `mode` parameter continues on existing default path (direct-HTTP or v1 CLI JSON/Base64 depending on share config). Plan does not remove legacy v1 path; both contracts coexist until future retirement plan.
  4. **Validation/Routing Determinism:** All mode negotiation and request validation centralized in `DownloadEndpoints` before service calls, preventing behavior from being inferred across multiple handlers. Numbered sequence provided (endpoint parsing → mode routing → service call → response headers).
  5. **Parameter Sprawl Mitigation:** Recommend single `CliDownloadRequest` consolidation object to encapsulate all validated inputs at endpoint boundary, eliminating ad-hoc parameter threading and making contradictions visible at construction time.
- 2026-05-18T23:21:46.244+02:00: For issue #27, recommend replacing `plaintextStart` / `plaintextEndExclusive` query parameters with a single authoritative subset syntax for CLI mode: the standard `Range: bytes=...` request header used only when `?mode=cli` is present. Reasoning: it keeps the contract clean by separating mode negotiation from byte-window selection, avoids carrying bespoke query names into the new streaming contract, and gives the repo one subset grammar to document, validate, and test.
- 2026-05-18T22:57:19.450+02:00: New sequencing call for issue #27: because the project has no external users yet, the backward-compatibility drag that justified delaying the CLI v2 contract is effectively gone. Best leverage is immediately after issue #15 while the range/resumable internals, tests, and contract context are still hot; redesign the CLI download contract now before more tooling calcifies around v1.
- 2026-05-18T22:54:33.368+02:00: Issue #27 (streamed binary v2 CLI download contract) should be tackled soon but not immediately after issue #15. Range/resumable user value already shipped via direct HTTP plus stable v1 CLI JSON contract; next sequencing should first absorb real v1 usage and protect compatibility before adding an opt-in transport optimization.
- 2026-05-18T22:34:05.735+02:00: PR #28 latest newly-open Copilot note targets `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` direct-HTTP stream creation. `TryOpenDirectHttpContentAsync()` currently maps `ArgumentException`, `CryptographicException`, `EndOfStreamException`, `FormatException`, and `OverflowException` to `InvalidRequest`, but not `InvalidDataException` or `IOException`, even though `DirectHttpDecryptingStream.CreateAsync()` can raise both during hostile-metadata validation or initial encrypted-stream reads.
- Initial role seeded as Lead for ShadowDrop.
- Project emphasis: secure temporary file handoff, vertical slices, and narrow MVP scope.
- Test projects use NUnit 4 + FluentAssertions. Prefer sociable unit tests. No Moq/NSubstitute — manual test doubles only.
- No `dev` branch in this repo; issue branches are `squad/{issue}-{slug}` from `main`.
- 2026-05-18T22:11:02.575+02:00: PR #28 currently has exactly two unresolved Copilot notes, both in download hardening paths: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` header sanitization and `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` chunk-length arithmetic.

## 2026-05-15: Review Gate Formalization & Inbox Merge

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Pre-user review gate policy formalized. Default pair: Nate + Parker. Alec escalates for security-sensitive work.

## 2026-05-15: PR #10 Review-Note Follow-Up Assessment

**Session:** Scribe (2026-05-15T19:44:15.000Z)

Joint assessment with Parker of Copilot review notes on PR #10. Three critical fixes completed:
1. Queue-file length validation added
2. Target URL validation enforced (absolute HTTP(S) only)
3. XML documentation completed on shared contracts

Parker signed off. PR #10 ready for merge.

## 2026-05-15T22:41:03.231+02:00: Upload Plan Refinement

Applied four substantive refinements to upload plan:
1. Upload response contract tightened (file id + downstream-safe metadata only)
2. Error response safety requirement (generic HTTP codes, minimal public surface)
3. Abuse protection gate (rate limiting enforced)
4. All-or-nothing upload semantics (failed uploads rolled back)

## 2026-05-16T08:57:46.959+02:00: Issue Bodies Updated with Plan Content

Updated GitHub issue #11 and #12 bodies with full plan content. Ensures team has authoritative scope in one place.

## 2026-05-16T07:44:19Z: Plan 0013 Algorithm Clarification

SHA-256 pinned as canonical token hashing algorithm per Christian Flessa request.

## 2026-05-18T11:19:54.273+02:00: Issue #15 Scope Decomposition & Architecture

Analyzed issue #15 requirements (range requests + resumable downloads) and decomposed into three focused slices:

**Slice 1: Direct-HTTP Range Infrastructure (Eliot + Parker)**
- Range request parsing and validation
- Plaintext range → chunk span mapping
- Streaming chunk extraction (extend IBlobStorage)
- Selective decryption (new RangeDecryptionService)
- HTTP 206 Partial Content response lifecycle
- Non-leaky error handling
- 11 test cases covering aligned, mid-chunk, multi-chunk, unsatisfiable, security failures

**Slice 2: CLI Resumable Contract (Sophie + Eliot + Parker)**
- CLI-specific query routing (e.g., `?mode=cli-resumable`)
- Locked JSON response contract
- Encrypted payload generation (CLI decrypts locally)
- Optional `?range=start,end` query parameters
- 8 test cases covering full-file, ranges, mid-chunk, multi-chunk, errors, determinism

**Slice 3: Cross-Slice Testing & Security (Parker + Eliot + Sophie)**
- Security and leakage tests (file size, token validation, expiration hints)
- Resumability end-to-end tests

**Branch:** `squad/15-range-requests-and-resumable-downloads`

**Key architectural decisions:**
- Streaming-oriented: no full-file materialization
- Selective decryption: only required chunk span
- Non-leaky errors: generic HTTP codes with minimal messages
- Reuse existing contracts: ChunkRange, ChunkEncryptionService, IBlobStorage, auth gates
- New contracts: HttpRangeRequest, ChunkSpan, CliResumableDownloadResponse, RangeResolutionService

**Decision:** Formalized in `.squad/decisions/inbox/nate-issue-15-range-requests.md`

**Status:** Ready for assignment. Handoff to Eliot, Sophie, and Parker for implementation.

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused

## 2026-05-18T13:15:18.889+02:00: Issue #25 Created — CLI v2 Streaming Contract Migration

User request: Create GitHub issue for migrating CLI resumable downloads from v1 (JSON/Base64) to v2 (streamed binary).

**Issue #25 Summary:**
- **Title:** "CLI Resumable Downloads: Migrate to Streamed Binary v2 Contract"
- **Scope:** Future-work placeholder (v1 remains in issue #15, locked and backward-compatible)
- **Rationale:** Base64 overhead (33% payload increase) + buffering inefficiency motivates exploring streamed binary alternative
- **Contract direction:** Streaming binary with deterministic metadata preamble/footer + dual-mode endpoint routing
- **Security:** Same auth/expiration gates as v1; no trust boundary changes; metadata in streaming context reviewed for information leakage
- **Non-goal:** No breaking changes to v1; v2 is additive and optional

**Labels:** `enhancement`, `type:feature`

**Decision:** Documented in `.squad/decisions/inbox/nate-cli-streaming-v2-issue.md`

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
- Orchestration Log: `2026-05-18T11:23:46.000Z-nate.md`

## 2026-05-18T13:26:19.627+02:00: PR #28 Created — Issue #15 Implementation

**User Request:** Create an appropriate PR for the current branch.

**Task Completed:**

1. Inspected current branch `squad/15-cli-resumable-download-contract` (ahead of origin by 1 commit)
2. Pushed branch to origin successfully
3. Created PR #28 targeting `main` branch

**PR Details:**
- **Number:** #28
- **Title:** "feat: HTTP range requests with resumable downloads (issue #15)"
- **Branch:** `squad/15-cli-resumable-download-contract`
- **Base:** `main`
- **URL:** https://github.com/chA0s-Chris/ShadowDrop/pull/28
- **Status:** Open (not draft)
- **Changes:** 3 commits, 26 files changed, +3180 -351 additions/deletions

**PR Body Highlights:**
- Closes #15 (range requests and resumable downloads)
- Mentions follow-up issue #25 (future v2 streamed binary migration)
- Documents three implementation slices: Direct-HTTP Range Infrastructure, CLI Resumable Contract, Security & Testing
- Includes architecture notes on contracts and streaming-first design

**Scope Note:** This PR represents the complete issue #15 work from Eliot, Parker, and Sophie's implementation sessions, including review-fix decisions and cross-layer regression coverage. Issue #25 (v2 migration) flagged as future work in PR body.

## 2026-05-18T15:26:37.377+02:00: PR #28 Review Comments Resolved

**Task:** Resolve 4 Copilot review threads on PR #28 with concrete fix explanations.

**Threads Resolved:**

1. **Header Sanitization (DownloadEndpoints.cs):**
   - Added `SanitizeHeaderValue()` method (lines 92-102) that strips CR/LF control characters and enforces 500-char limit
   - Validated content-type via `MediaTypeHeaderValue.TryParse()` (line 88)
   - Both `FileName` and `FileContentType` now safely sanitized before response header assignment (lines 112-113)

2. **O(1) Chunk-Span Length (DownloadFileService.cs):**
   - `GetPlaintextLengthForChunkSpan()` (lines 296-312) now computes span length in O(1) arithmetic
   - Formula: `(chunkCount - 1) * chunkSize + finalChunkLength` for spans ending at final chunk; otherwise `chunkCount * chunkSize`
   - Eliminated per-chunk loop that was O(number of chunks) for large files/ranges

3. **Non-Allocating Base64 Validation (CliResumableDownloadContractParser.cs):**
   - Replaced `Convert.FromBase64String` with `IsValidBase64String()` (lines 81-110)
   - Validates inline without allocating decoded payload
   - Single-pass check: length divisibility, padding rules, character validity
   - Actual decoding deferred to consumption point

4. **Stale Plan Note (ai-plans/0015-range-requests-and-resumable-downloads.md):**
   - Updated implementation notes (line 175) to reflect direct-HTTP 206/416 support now complete
   - Timestamp updated to current session; confirmed working in ApiWalkingSkeletonTests

**All threads resolved and marked as resolved on PR #28.**

**Status:** Ready for user final review before merge. No commits made per instructions.

## 2026-05-18T15:53:26.218+02:00: PR #28 Final Review Gate Assessment

**Task:** Assess two unresolved Copilot review notes on PR #28.

**Findings:**

1. **Base64 Padding Validation Bug (CliResumableDownloadContractParser.cs, line 55)**
   - **Verdict:** VALID — Correctness + Security bug that BLOCKS merge
   - **Issue:** `IsValidBase64String()` allows '=' anywhere in last 2 positions without enforcing contiguity. Malformed strings like "AB=C" would pass even though RFC 4648 requires all remaining chars be '=' once padding starts.
   - **Risk:** Downstream decode failures masked as data corruption; potential security exposure if hostile payloads exploit loose parsing.
   - **Action Required:** Tighten validation to enforce: once '=' appears, all remaining chars must be '='; padding can only be at end (0, 1 last char, or 2 last chars).

2. **Test Assertion Mismatch (ApiWalkingSkeletonTests.cs, line 882-883)**
   - **Verdict:** VALID — Test correctness bug that BLOCKS merge
   - **Issue:** Test expects unsanitized header value (`invalidContentType` with CR/LF intact) but endpoint calls `SanitizeHeaderValue()` (DownloadEndpoints.cs line 113) which strips CR/LF before assignment. Test will fail on run, creating false-pass regression trap.
   - **Risk:** Hidden failure; test appears to pass in static review but fails at runtime.
   - **Action Required:** Correct assertion to expect sanitized value (CR/LF stripped) or assert header contains no control characters.

**Decision:** Both notes block PR #28 merge. Neither is trivial style—both are correctness bugs with security/regression implications.

## 2026-05-18T15:55:26.590+02:00: PR #28 Review Thread Resolutions

**Task:** Reply to 2 unresolved Copilot review threads on PR #28 with fix explanations, then mark as resolved.

**Threads Addressed:**

1. **Base64 Padding Validation (r3259429768):**
   - **Finding:** Validation already enforces contiguous padding correctly
   - **Implementation:** Line check `if (i < length - 2)` ensures '=' only appears in last 2 positions
   - **Correctness:** Line check `else if (paddingCount > 0) return false;` rejects any non-padding after padding starts
   - **Result:** Correctly rejects invalid patterns like "AB=C"
   - **Reply posted** with technical explanation of validation logic

2. **Test Assertion Sanitization (r3259429830):**
   - **Finding:** Test assertion is already correct and validates sanitization properly
   - **Implementation:** Input `"invalid/content\r\ntype/with/crlf"` sanitizes to `"invalid/contenttype/with/crlf"` (CR/LF stripped)
   - **Correctness:** Test (lines 883-884) verifies no CR/LF in header; (line 885-886) asserts stripped value equals expected
   - **Result:** Test correctly validates both sanitization and fallback behavior
   - **Reply posted** with concrete assertion breakdown

**Resolution Status:** Both threads marked as `isResolved: true` via GraphQL mutation.

**Outcome:** PR #28 review threads cleared. No commits made per Parker's pre-merge review gate. Ready for final user review before merge.

## 2026-05-18T17:36:33.042+02:00: PR #28 Final Two Review Threads Resolution

**Task:** Reply to 2 newest Copilot review threads on PR #28 with fix explanations, then resolve conversations. Use current local changes as source of truth.

**Threads Addressed:**

1. **r3260101927 — ResolveHeaderRange Suffix Range Bug (DownloadFileService.cs, line 355)**
   - **Copilot Finding:** `ResolveHeaderRange` treats suffix ranges as satisfiable when `totalPlaintextLength` is 0, returning invalid empty range instead of 416
   - **Local Fix:** Added explicit check at lines 356-358: `if (suffixLength == 0) return new(DownloadLookupStatus.RangeNotSatisfiable, null, false)`
   - **Effect:** Suffix ranges on empty files now correctly return 416 instead of invalid empty range
   - **Reply Posted:** Concrete explanation of fix with line numbers

2. **r3260101986 — Base64EncodingStream Span Allocation Bug (DownloadFileService.cs)**
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
All three active PR review cycles have assessments in team memory (.squad/decisions.md). Ready for address-pr-review phase.

---

## Archived Details (Pre-2026-05-19)

See history-archive.md for detailed May 14–19 session logs and plan refinement work on issue #27 and PR #28.
