# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Lead for ShadowDrop.
- Project emphasis: secure temporary file handoff, vertical slices, and narrow MVP scope.
- Test projects use NUnit 4 + FluentAssertions. Prefer sociable unit tests. No Moq/NSubstitute — manual test doubles only.
- No `dev` branch in this repo; issue branches are `squad/{issue}-{slug}` from `main`.

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
