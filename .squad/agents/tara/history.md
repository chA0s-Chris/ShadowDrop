# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- 2026-05-19T18:54:27.229+02:00: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` must emit streamed CLI numeric metadata headers with `CultureInfo.InvariantCulture` so wire values stay canonical ASCII digits regardless of host/request culture; regression coverage lives in `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs` with a non-default culture.
- 2026-05-19T18:54:27.229+02:00: `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs` must validate that `TotalPlaintextSize`, derived chunk count, `ChunkSize`, and `FinalChunkPlaintextLength` agree on the last chunk length before trusting streamed metadata; focused regression coverage lives in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadResponseParserTests.cs`.
- Initial role seeded as Platform Dev for ShadowDrop.
- The concept requires one-container Docker distribution plus x64 and arm64 delivery for server and CLI artifacts.
- 2026-05-14T20:06:45.536+02:00: `ShadowDrop.slnx` already contains the three production projects plus all three test projects, so issue work should preserve existing solution membership instead of recreating it.
- 2026-05-14T20:06:45.536+02:00: Baseline dependency wiring is centralized in `Directory.Packages.props`; `LiteDB` is scoped to `src/ShadowDrop.Api/ShadowDrop.Api.csproj`, while `System.CommandLine` and `Spectre.Console` are scoped to `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj`.
- 2026-05-14T20:06:45.536+02:00: Native AOT visibility for the CLI lives in `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj` via `IsAotCompatible`, `PublishAot`, and `InvariantGlobalization`, with `linux-x64` smoke publish as the baseline validation path.
- 2026-05-14T20:21:46.760+02:00: Created PR #5 for issue #1 targeting `main` (no `dev` branch in this repo). PR body mirrors acceptance criteria with validation checklist; linked and closes issue #1.
- 2026-05-14T22:55:52.308+02:00: For local history surgery on `squad/2-chunked-aes-gcm-crypto-spike`, the safe path was to rebase the trailing `.squad/` docs commit onto `afb29a7` and then restore `src/ShadowDrop.Shared/Crypto/*` plus `tests/ShadowDrop.Shared.Tests/Crypto/*` with `git cherry-pick --no-commit`, leaving the code/test changes staged for review while removing commits `ff9b8ff` and `546e543` from branch history.
- 2026-05-15T00:01:13.117+02:00: The crypto hot path in `src/ShadowDrop.Shared/Crypto/ChunkEncryptionService.cs` serializes `Guid` values into stackalloc buffers for AAD/HKDF info; use `Guid.TryWriteBytes` instead of `Guid.ToByteArray()` there to avoid per-call heap allocations. Regression coverage for derived-key context mismatches lives in `tests/ShadowDrop.Shared.Tests/Crypto/ChunkEncryptionServiceTests.cs`.
- 2026-05-15T00:16:55.489+02:00: `src/ShadowDrop.Shared/Crypto/EncryptedChunk.cs` should follow the same buffer-encapsulation pattern as `FileEncryptionContext`: public `byte[]` access returns a copy, while `ChunkEncryptionService` consumes an internal `ReadOnlySpan<byte>` so encrypt/decrypt stay zero-copy inside the assembly. Single-chunk invariants for `ChunkRange` are regression-covered in `tests/ShadowDrop.Shared.Tests/Crypto/ChunkRangeMappingTests.cs`.

## Squad Transition — 2026-05-14T18:13:02Z

Issue #1 decision (dependency & AOT strategy) merged to team decisions by Scribe.

## 2026-05-15: Cryptographic Optimization Decisions Merged

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Two crypto hot-path decisions merged into `decisions.md`:
1. Direct Guid span writes (allocation-free AAD/HKDF blobs, matches existing span style)
2. Encrypted chunk buffer ownership (copy at boundary, internal span for no-copy path)

Optimizations preserve allocation-free behavior and maintain trust boundaries. Merged with full context into canonical decisions.

- **Status:** Merged; part of pre-user review gate security scope
- 2026-05-15T16:17:18.120+02:00: `tests/ShadowDrop.Shared.Tests/Contracts/FileMetadataContractTests.cs` should cover both full JSON round-trip for `FileMetadataContract` and deserialization when optional `plaintextSha256` is omitted; property-name-only coverage was not enough for reviewer expectations on issue #4.

## 2026-05-15: Issue #4 Pre-Review Gate Cycle

**Session:** Scribe (2026-05-15T14:31:20.000Z)

Assigned to revise Eliot's Issue #4 implementation after Parker rejected for missing FileMetadataContract coverage. Added round-trip and optional-field test cases per rejection criteria. Revision passed Parker re-review.

- **Status:** Gate PASSED; shared contract design finalized and merged to `decisions.md`
- **Next:** PR #4 ready for user review

## 2026-05-15T17:20:32.944+02:00: PR #10 Created for Issue #4

Created GitHub PR #10 targeting `main` for branch `squad/4-define-shared-contracts-and-constants`. PR title/body mirrors issue #4 context with implementation summary and design notes. Branch verified clean; no duplicate PR existed prior.
- 2026-05-18T18:02:41.646+02:00: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` should sanitize persisted filenames once and reuse the sanitized value for both `X-ShadowDrop-File-Name` and `Content-Disposition` `FileNameStar`, so hostile metadata cannot inject headers or crash response header validation.
- 2026-05-18T18:02:41.646+02:00: `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` should let `CliDecryptJsonStream` fall back to the base `Stream` span shim instead of overriding `Read(Span<byte>)` with a per-call array allocation; focused regression coverage remains in `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`.
- 2026-05-18T22:12:59.106+02:00: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` should use one `Char.IsControl`-based fast-path/sanitizer rule for mirrored header values and filenames; ASCII-only pre-scans miss persisted C1 controls like `\u0085`, so HTTP regressions belong in `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`.
- 2026-05-18T22:12:59.106+02:00: `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` now treats corrupt chunk metadata as fail-closed input: final chunk length math must stay checked and resolve to `1..ChunkSize`, with focused hostile-metadata coverage in `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`.
- 2026-05-19T12:11:29.955+02:00: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` `TryCreateDownloadRequest` must distinguish absent `mode` query param from present-but-empty; check `request.Query.ContainsKey(ModeQueryParameterName) && IsNullOrWhiteSpace(mode)` before the DirectHttp fallthrough to return null (→ 400) for the explicit-empty case.
- 2026-05-19T12:11:29.955+02:00: `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs` bearer-token tests (`WhenBearerTokenIsExpired`, `WhenBearerTokenIsWrong`) had the bearer token passed as the `mode` argument (3rd param) instead of `authorizationBearerToken` (4th param); they passed by coincidence because null bearer triggers Forbidden too — fixed to null/bearerToken order so they exercise the intended expired/wrong-hash branches.

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

- 2026-05-19T18:02:15.900+02:00: `src/ShadowDrop.Api/Downloads/DownloadFileService.cs` string overloads for download negotiation should mirror `DownloadEndpoints.TryCreateDownloadRequest`: only `null` means omitted/direct HTTP, `cli` selects streamed CLI, and any explicit blank or unknown mode must return `InvalidRequest` fail-closed. Regression coverage lives in `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`; stale `CliDecrypt` test names should be updated to `StreamedCli` wording in both API and service tests.

## 2026-05-19T16:02:15.900Z — Scribe: Invalid Mode Overload Fail-Closed

**Agents involved:** Tara, Parker  
**Topic:** Public download negotiation parity

Tara delivered invalid-mode fail-closed fix to `DownloadFileService.ResolveAsync(string? mode, ...)`:
- Explicit invalid/blank mode values now return `InvalidRequest` instead of silently falling back to direct HTTP
- Regression coverage added at both service and API layers (pinned contract)
- Test naming clarified: renamed stale `CliDecrypt` test names to `StreamedCli` wording
- All targeted API tests pass; solution build clean

Parker reviewed and approved correctness and coverage.

**Decision tracked:** `.squad/decisions.md` → "Invalid Mode Overload Fail-Closed"
- 2026-05-19T18:34:53.970+02:00: `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs` now treats streamed numeric metadata headers as canonical ASCII digit-only integers only; parse with `NumberStyles.None` + `CultureInfo.InvariantCulture` and reject whitespace/sign-prefixed values fail-closed. Regression coverage for malformed CLI download headers lives in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadResponseParserTests.cs`.

## 2026-05-19T16:34:53Z — Scribe: CLI Header Parsing Hardening Complete

**Agents involved:** Tara, Parker, Nate  
**Topic:** Strict CLI download metadata header parsing

Tara delivered strict CLI response header parser hardening:
- `src/ShadowDrop.Cli/Downloads/CliDownloadResponseParser.cs`: now accepts only canonical ASCII digit-only integers
- Rejects leading/trailing whitespace, plus signs, minus signs, and non-digit characters
- Uses `NumberStyles.None` + `CultureInfo.InvariantCulture` for invariant parsing
- Regression test coverage in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadResponseParserTests.cs`

**Rationale:** Transport metadata requires strict parsing to keep local and CI behavior identical, prevent malformed headers from passing normalization, and maintain fail-closed validation for hostile or sloppy intermediaries.

**Status:** Completed, approved by Parker, ready for integration.

**Decision tracked:** `.squad/decisions.md` → "Strict CLI download header parsing"

## 2026-05-19 — Decision Archived & Merged

**Event:** Scribe merged Tara's PR #29 fix decisions into canonical record and archived inbox entries.

**Impact on Tara:**
- Decision "Treat streamed CLI numeric metadata headers as culture-invariant wire values…" is now part of permanent team record
- Regression coverage for invariant culture test and final-chunk validation test integrated
- Both API and CLI test suites validated cleanly
- Ready for next feature cycle

## 2026-05-19T17:32:56Z — PR #29 DownloadAsync Resume Session Preflight Validation (Nate Assessment)

**Event:** Nate assessed latest Copilot review note on PR #29 and recommended fail-closed preflight validation for resumable download sessions.

**Issue:** `CliDownloadSession.DownloadAsync()` trusts `DurablePlaintextLength` and seeks seekable destination streams to that offset without validating `destination.Length == DurablePlaintextLength`. Stale caller state can create gaps or resume from wrong plaintext boundaries.

**Impact on Tara:**
- `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs` requires preflight validation before HTTP request
- For seekable destinations, must fail closed unless `destination.Length == DurablePlaintextLength`
- Throw state exception with clear contract message
- Add regression tests for seekable streams with length shorter and longer than durable state
- Decision tracked: `.squad/decisions.md` → "Nate Decision — PR #29 DownloadAsync Resume Session Preflight Validation"

## 2026-05-19T19:35:51Z — Resume Destination Length Validation Implemented

**Event:** Tara implemented fail-closed preflight validation for `CliDownloadSession` resumable downloads.

**Decision:** When resuming into a seekable destination stream, the stream length must exactly equal `DurablePlaintextLength` before any seek or HTTP resume request. If shorter or longer, throw `InvalidOperationException`.

**Implementation Details:**
- Modified `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs` to validate destination.Length == DurablePlaintextLength
- Does not issue resume request if validation fails (fail-closed)
- Added regression tests in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadSessionTests.cs`:
  - Test for shorter destination (reject before request)
  - Test for longer destination (reject before request)
- Decision tracked: `.squad/decisions.md` → "Tara Decision — Resume Destination Length Must Match Durable Plaintext"
- Status: Implementation complete, regression tests passing

- 2026-05-19T19:35:51.788+02:00: `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs` resume flow must fail closed before any seek/network call when a seekable destination stream length differs from `DurablePlaintextLength`; shorter and longer mismatch regressions belong in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadSessionTests.cs`.

## 2026-05-29: Issue Prioritization — Deferred Issues Pending Scope

**Event:** Nate prioritized open GitHub issues (#18, #19, #20) and recommended #18 as next target.

**Assignment:** Tara holds #19 (Docker) and #20 (Native AOT) pending scope clarification from Christian.
- **Reason:** Both #19 and #20 lack acceptance criteria and scope definition
- **Blocker:** #19 requires Christian to define Docker registry, base image policy, build context, health check contract, multi-architecture support, CI/CD hookup
- **Blocker:** #20 requires Christian to define target platforms, artifact format, notarization/signing, CI/CD integration, compatibility matrix
- **Sequencing:** Will prioritize #19 after #18 feature completes, #20 after #19 provides canonical container image
- **Decision:** Recorded in canonical `.squad/decisions.md`

## 2026-05-29T08:23:50Z — PR #32 Created for Issue #18 (Interactive Spectre.Console UX)

**Event:** Tara created PR #32 from branch `squad/0018-interactive-spectre-console-ux`.

**Outcome:**
- PR #32: "Interactive Spectre.Console UX"
- URL: https://github.com/chA0s-Chris/ShadowDrop/pull/32
- Base: main
- Status: Ready for review; Copilot review requested

**Next:** Pending Copilot and team review feedback on interactive console improvements for issue #18.
