# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Tester for ShadowDrop.
- The concept puts special emphasis on resumable downloads, token expiry, revocation, and multi-file share behavior.
- 2026-05-14T23:22:54.266+02:00: The chunked AES-256-GCM spike lives in `src/ShadowDrop.Shared/Crypto/` and its acceptance coverage lives in `tests/ShadowDrop.Shared.Tests/Crypto/`.
- 2026-05-14T23:22:54.266+02:00: Current crypto acceptance evidence is implementation-plus-tests: 26 shared crypto tests pass, and `dotnet publish src/ShadowDrop.Cli/ShadowDrop.Cli.csproj -c Release -r linux-x64 --self-contained true` succeeds for Native AOT validation.
- 2026-05-14T23:26:13.429+02:00: Crypto review found the biggest remaining test gaps in `ShareSecret.FromBytes`, `ChunkEncryptionService.GetChunkRange` guard rails, and secret/key disposal behavior; current coverage is measured with `dotnet test tests/ShadowDrop.Shared.Tests/ShadowDrop.Shared.Tests.csproj --collect:"XPlat Code Coverage"`.
- 2026-05-14T23:28:54.563+02:00: Added targeted crypto regression tests in `tests/ShadowDrop.Shared.Tests/Crypto/` for `ShareSecret.FromBytes`, disposed secret/content-key behavior, `GetChunkRange` invalid inputs, and constructor validation on `FileEncryptionContext`/`ChunkMetadata`.
- 2026-05-14T23:28:54.563+02:00: After the new tests, shared crypto coverage shows `ShareSecret.cs`, `FileEncryptionContext.cs`, and `ChunkMetadata.cs` at 100% line/branch coverage; `ChunkEncryptionService.cs` and `ContentKey.cs` still retain a few uncovered defensive branches.

## Cross-Agent Updates — 2026-05-14T18:43:13Z

**From Nate (Issue #2 Crypto Spike):**  
Crypto spike complete. Handoff: implement 26 test cases in `tests/ShadowDrop.Shared.Tests/Crypto/` per spec in `.squad/decisions.md` (Encryption section). Happy-path: 14 cases (single/multi-chunk round-trips, range alignment, sub-chunk). Failure-path: 12 cases (tamper ciphertext, metadata fields, wrong key/context). Use NUnit 4 + FluentAssertions. No mocks. Branch `squad/2-chunked-aes-gcm-crypto-spike` ready.

## Learnings — 2026-05-15T12:47:15.427+02:00

- **Key test file:** `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs` — all API walking skeleton tests live here.
- **TestApiFactory pattern:** The factory accepts `enableAdminOperations`, `enablePublicDownloads`, and `withBootstrapToken` boolean params; env vars are saved/restored per-instance. Tests must remain `[NonParallelizable]` because env vars are process-wide.
- **WebApplicationFactory startup exception behavior (.NET 10):** When `PrepareStartup` throws (e.g., missing bootstrap token), `WebApplicationFactory` catches the startup exception internally. `CreateClient()` subsequently throws `InvalidOperationException: "The server has not been started…"` (not the original startup exception). `DisposeAsync()` does NOT rethrow the startup exception when assertions have already passed. The startup exception surfaces as an unobserved background task warning in the test output. Tests should assert `Throw<InvalidOperationException>()` without a message check for this scenario.
- **Coverage gaps addressed (2026-05-15):** Wrong bearer token → 401 (`ManagementEndpoint_ShouldReturn401_ForWrongBearerToken`); public downloads disabled → 404 (`PublicDownloadEndpoint_ShouldReturn404_WhenPublicDownloadsAreDisabled`); upload route auth (`UploadRoute_ShouldRequireValidAdminBearerToken`); startup failure without bootstrap token (`Startup_ShouldFail_WhenBootstrapAdminTokenIsMissingOnFirstBoot`).
- **Build+test command:** `dotnet build tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj && dotnet test tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj --no-build`

## Learnings — 2026-05-15T14:30:00.000+02:00

- **AdminTokenService is conditional on EnableAdminOperations:** `PrepareStartup` only resolves `AdminTokenService` when `options.ApiExposure.EnableAdminOperations` is `true`. When admin operations are disabled, the service is never instantiated, `EnsureBootstrapCredential` is never called, and startup succeeds without a bootstrap token. This is intentional — no admin token is needed if the admin API surface is not exposed.
- **Coverage gaps addressed (2026-05-15, second pass):** Relative-path config binding (`Config_RelativePaths_ShouldBeResolvedToAbsolutePathsUnderContentRoot`); startup succeeds without bootstrap token when admin ops disabled (`Startup_ShouldSucceed_WhenAdminOperationsAreDisabled_EvenWithoutBootstrapToken`); bootstrap failure leaves env clean for subsequent startup (`Startup_BootstrapFailure_ShouldLeaveEnvironmentClean_ForSubsequentStartup`). Test count: 9 → 12, all passing.
- **TestApiFactory useRelativePaths support:** Added `useRelativePaths` constructor parameter; sets relative path strings for env vars, sets `_temporaryRootDirectory = String.Empty` so base cleanup is skipped, captures content root via `IWebHostEnvironment` in `Dispose` before `base.Dispose()`, then cleans up the relative test directory by absolute path after `base.Dispose()`.

## 2026-05-15: Test Coverage & Quality Decisions Merged

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Two test quality decisions merged into `decisions.md`:
1. API test coverage gaps closed (4 scenarios: public download enabled, relative-path cleanup, startup failure, bootstrap env restoration)
2. WebApplicationFactory startup exception behavior documented (startup failure test semantics on .NET 10)

Silent bug fixed (relative metadata path cleanup scope). TestApiFactory constructor extended with conditional flags. Merged with full context into canonical decisions.

- **Status:** Merged; part of default review pair role
- **Note:** Default reviewer with Nate on all future pre-user review gate work

## 2026-05-15: Issue #4 Pre-Review Gate Cycle

**Session:** Scribe (2026-05-15T14:31:20.000Z)

Default reviewer on Issue #4 (Nate + Parker pair per pre-user review gate policy). Rejected Eliot's initial implementation for missing FileMetadataContract coverage (round-trip / optional-field test cases). Assigned Tara to revise (per lockout semantics). Re-reviewed Tara's revision; coverage gap closed and signed off.

- **Status:** Gate PASSED; decision design merged to `decisions.md`
- **Next:** PR #4 ready for user review

## 2026-05-15: PR #10 Review-Note Follow-Up Assessment & Re-Review

**Session:** Scribe (2026-05-15T19:44:15.000Z)

Parker + Nate joint assessment of remaining Copilot review notes on PR #10. Identified three critical fixes needed:
1. Missing queue-file length validation (bounds checking in parser validator)
2. Insufficient target URL validation (must be absolute HTTP(S) only)
3. Incomplete XML documentation on shared contracts

Assessment passed to Eliot for implementation. After Eliot completed fixes on current branch:
- Re-reviewed implementation against identified items
- All three items verified: length validation ✓, URL validation ✓, XML docs ✓
- Signed off on implementation completeness

Orchestration logs written. Decision "Shared Queue Contract Shape" documented in `decisions.md`.

- **Role:** Secondary reviewer on Nate + Parker pair assessment

## 2026-05-16T07:31:13Z: Plan 0013 Security Review — Ready for Implementation Gate

**Session:** Scribe (cross-agent notification)

Plan 0013 (share creation, expiration, hashed bearer tokens) has all Nate and Alec review feedback formalized. Five surgical clarifications now binding:

1. Token entropy: 32 bytes minimum
2. Plaintext confidentiality: returned once, never logged/cached server-side
3. Expiration: soft validation, deferred to later slices
4. Revocation/cleanup fields: initialized at creation
5. Mode/token combinations: explicit rejection rules

**Review gate application:** As default reviewer pair (Nate + Parker), will assess implementation against all criteria. Alec escalation required for token generation, hashing, and confidentiality verification.

**Plan boundaries:** Slice owns creation + persistence. Does NOT include download endpoint, validation, revocation, or cleanup. All belong to later slices.

**Status:** Plan ready for backend team (Eliot or assigned) implementation. Gate will apply on PR submission.

- 2026-05-17T23:05:01.413+02:00: Direct-HTTP download key material now routes through `DownloadFileService.WithDecodedDirectHttpKeyMaterialAsync`, so decoded base64 bytes are zeroed when blob open or other pre-transfer initialization fails.
- 2026-05-17T23:05:01.413+02:00: Regression coverage for the pre-transfer zeroing path lives in `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs` and the focused validation command is `dotnet test tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj --filter DownloadFileServiceTests --no-restore`.
- 2026-05-17T23:22:19.313+02:00: `DownloadFileService.DirectHttpDecryptingStream` now derives and retains one `ContentKey` per stream in `CreateAsync`, reuses it across chunk reads, and zeroes both the retained content key and the decoded share secret on stream disposal.
- 2026-05-17T23:22:19.313+02:00: Focused regression coverage in `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs` now proves the direct-HTTP stream reuses the same derived content key across chunk boundaries and zeros retained key material on dispose; `dotnet test tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj --filter DownloadFileServiceTests --no-restore` passes with 6 tests.
- 2026-05-18T08:27:12.481+02:00: Added upload-reservation expiry regression coverage in `tests/ShadowDrop.Api.Tests/Uploads/UploadPersistenceServiceTests.cs` and `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`; focused validation command `dotnet test tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj --filter "FullyQualifiedName~UploadPersistenceServiceTests|FullyQualifiedName~ApiWalkingSkeletonTests" --no-restore` passes with 48 tests.
- 2026-05-18T08:39:49.512+02:00: Added PR #24 regression coverage proving concurrent upload attempts against one reserved file id yield one successful claim and one `UploadValidationException` cleanup path in `tests/ShadowDrop.Api.Tests/Uploads/UploadPersistenceServiceTests.cs`; full API test project passes with 71 tests.
- 2026-05-18T08:39:49.512+02:00: `DownloadFileService.DirectHttpDecryptingStream` now applies the same key/secret zeroing and underlying-stream cleanup on synchronous `Dispose()` as on `DisposeAsync()`, with regression coverage in `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`.

- 2026-05-18T11:19:54.273+02:00: Issue #15 coverage split is now explicit: shared crypto tests already cover chunk-span math and plaintext slice correctness, while API walking tests now pin non-partial generic failures for invalid, expired, missing-bearer, and invalid-key range-shaped download requests. HTTP 206/416 coverage is still blocked until the public download endpoint parses Range and emits partial-response headers.

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused
- 2026-05-18T13:15:18.889+02:00: Added regression coverage for issue #15 so `DownloadFileService` CLI responses are asserted against `ContractsJsonSerializerContext` output and `CliResumableDownloadContractParser` accepts very large valid chunk-span metadata without falling back into the previous large-span failure mode; focused API/shared/CLI test projects all pass.

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
- Orchestration Log: `2026-05-18T11:23:46.000Z-parker.md`

## 2026-05-18T15:26:37.377+02:00 — Issue #15 PR #28 Review Complete

**Context:** Reviewed four Copilot PR review notes on PR #28 (issue #15 range requests) and verified all fixes landed correctly.

**Four PR Notes Reviewed:**
1. **Header injection security (DownloadEndpoints.cs:102)**: FileName and FileContentType written to headers without sanitization → fixed with `SanitizeHeaderValue` (strips CR/LF, truncates to 500 chars) and `GetResponseContentType` (validates media type, fallbacks to application/octet-stream)
2. **O(n) performance bottleneck (DownloadFileService.cs:978)**: Chunk-span plaintext length computed with per-chunk loop → fixed with `GetPlaintextLengthForChunkSpan` using O(1) arithmetic
3. **Base64 allocation waste (CliResumableDownloadContractParser.cs:75)**: `Convert.FromBase64String` allocated full decoded payload for validation → fixed with `IsValidBase64String` character-level validation without allocation
4. **Outdated plan documentation (0015-range-requests-and-resumable-downloads.md:177)**: Plan said "Direct-HTTP Range / 206 Partial Content work remains outstanding" → updated to reflect completed implementation

**Regression Coverage Added:**
- `PublicDownloadEndpoint_ShouldSanitizeFileNameWithControlCharacters`: Proves CR/LF in filename does not allow header injection
- `PublicDownloadEndpoint_ShouldFallbackToOctetStream_WhenContentTypeIsInvalid`: Proves malformed content-type triggers safe fallback instead of 500 error

**Verification Result:**
- All 165 tests pass (91 API + 3 CLI + 71 Shared)
- All four fixes confirmed in uncommitted changes (ready to commit)
- No breaking changes detected
- Issue #15 implementation is complete and ready for merge

**Key File Paths:**
- Implementation: `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`, `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `src/ShadowDrop.Cli/Downloads/CliResumableDownloadContractParser.cs`
- Tests: `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs` (lines 813-883)
- Plan: `ai-plans/0015-range-requests-and-resumable-downloads.md`
