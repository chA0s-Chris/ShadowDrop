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

- **Status:** Gate PASSED; PR #10 ready for merge
- **Role:** Secondary reviewer on Nate + Parker pair assessment
