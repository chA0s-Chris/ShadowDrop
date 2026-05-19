# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

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
