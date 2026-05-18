# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Backend Dev for ShadowDrop.
- The MVP centers on upload, share, download, revoke, and cleanup workflows.

## Cross-Agent Updates — 2026-05-14T18:43:13Z

**From Nate (Issue #2 Crypto Spike):**  
Crypto design finalized. Handoff: implement production code in `src/ShadowDrop.Shared/Crypto/` per API spec in `.squad/decisions.md` (Encryption section). Key constraints: deterministic nonces from chunk index, HKDF-SHA-256 key derivation, 50-byte AAD binding, `stackalloc` for AAD, `IDisposable` + `CryptographicOperations.ZeroMemory` on key types. All AOT-safe (System.Security.Cryptography.HKDF, AesGcm). Parker will test to 26 cases. Branch `squad/2-chunked-aes-gcm-crypto-spike` ready for implementation.

## Learnings — 2026-05-15T12:47:15.427+02:00

### AdminTokenService & Program.cs Bug Fixes

**Files touched:** `src/ShadowDrop.Api/Program.cs`, `src/ShadowDrop.Api/Infrastructure/Security/AdminTokenService.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`

- **HostAbortedException → OperationCanceledException**: `HostAbortedException` inherits from `OperationCanceledException`. Catching only `HostAbortedException` misses plain `OperationCanceledException` thrown by `WebApplicationFactory`/test cancellations. The fix is to widen the catch to `OperationCanceledException`, which covers both.

- **LiteDB credential existence check**: `Query.EQ(nameof(AdminTokenCredential.Id), BootstrapCredentialId)` is a property-name query. LiteDB stores `Id` as `_id` internally, so a name-based `Query.EQ` on `"Id"` may not match. Always use `FindById` for ID-based lookups — it goes through LiteDB's identity-aware path. Consistent with `IsValidToken`.

- **LiteDB shared connection**: Default `ConnectionType.Direct` is exclusive — only one `LiteDatabase` instance can open the file at a time. In-process tests (WebApplicationFactory) + the service both need the file simultaneously (e.g., `BootstrapToken_ShouldBeStoredAsProtectedHash` opens a second `LiteDatabase` while the host is alive). Fix: `ConnectionType.Shared` in `ConnectionString`.

- **WebApplicationFactory startup failure exception shape**: When `Program.Main` catches the startup exception internally and returns (instead of rethrowing), the factory sees a clean exit but a server that was never configured. `CreateClient()` then throws `InvalidOperationException("The server has not been started...")` — NOT the original cause. The test assertion must match this observable exception, not the internal root cause message.

- **LiteDB version on this project:** 5.0.21 (in `Directory.Packages.props`). `ConnectionString.Connection = ConnectionType.Shared` is available in this version.

## Learnings — 2026-05-15T13:34:42.045+02:00

### AdminTokenService conditional initialization & LiteDB leak fix

**Files touched:** `src/ShadowDrop.Api/CompositionRoot/DependencyInjection.cs`, `src/ShadowDrop.Api/CompositionRoot/Startup.cs`, `src/ShadowDrop.Api/Infrastructure/Security/AdminTokenService.cs`

- **Conditional registration:** `AdminTokenService` is now only added to DI and resolved at startup when `options.ApiExposure.EnableAdminOperations == true`. Previously it was unconditionally registered and eagerly resolved via `PrepareStartup`, forcing the bootstrap path (env-var check, DB open) even when the admin surface was intentionally disabled.

- **LiteDB handle on constructor failure:** Wrapped `EnsureBootstrapCredential` in a try/catch in the `AdminTokenService` constructor. On any exception `_database.Dispose()` is called before rethrowing. This prevents the file handle from leaking on first-boot failure (e.g., missing env var), which was previously causing `Cannot open file` errors in retried or parallel test runs.

- **Test gap noted (not added to prod code):** `ShadowDropOptionsBinding.ResolvePath` supports both absolute and relative `LiteDbPath`/`LocalRoot` config values, but there is no test asserting that a relative path is correctly resolved against `ContentRootPath`. A unit test for `BindAndValidate` covering this case would make the contract explicit and protect against future regressions.

## 2026-05-15: Storage & Initialization Decisions Merged

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Two backend decisions merged into `decisions.md`:
1. LiteDB shared connection mode for `AdminTokenService` (concurrent test safety)
2. Conditional admin service initialization (lean deployment mode, file-handle leak fix)

Relative path resolution test gap documented. Merged with full context into canonical decisions.

- **Status:** Merged; ready for enforcement

## 2026-05-16T07:31:13Z: Plan 0013 Ready for Implementation — Eliot Handoff

**Session:** Scribe (cross-agent notification)

Plan 0013 (share creation, expiration, hashed bearer tokens) now has all security clarifications and binding acceptance criteria formalized in canonical `decisions.md`. Nate (Lead) and Alec (Security) have finalized five surgical refinements:

1. Token entropy floor: 32 bytes minimum
2. Plaintext token lifecycle: volatile, never persisted server-side
3. Expiration validation: deferred to later slices (soft/lazy validation)
4. Revocation/cleanup fields: initialized at creation (no lazy init)
5. Mode/token combinations: explicit rejection rules with tests

**Handoff:** Backend team (Eliot or assigned) owns implementation with binding acceptance criteria. Vertical slice scope: share creation + token persistence only. Does NOT include download, validation, revocation, or cleanup endpoints.

**Review gate:** Nate + Parker (default), Alec escalation required. Implementation gate enforced on PR submission.

## Learnings — 2026-05-15T16:17:18.120+02:00

### Shared queue and metadata contracts in ShadowDrop.Shared

**Files touched:** `src/ShadowDrop.Shared/Contracts/*.cs`, `src/ShadowDrop.Shared/Queue/*.cs`, `tests/ShadowDrop.Shared.Tests/Queue/QueueFileParserTests.cs`, `tests/ShadowDrop.Shared.Tests/Contracts/FileMetadataContractTests.cs`

- Shared protocol constants now live in `ShadowDrop.Contracts` and cover the direct-download header/query names, CLI config path segments, format versions, and the stable AES-256-GCM algorithm id.
- Queue contracts use nullable JSON-bound models plus an explicit `QueueFileParser.Validate` pass instead of constructor-only invariants; this keeps deserialization stable while still surfacing CLI-friendly validation errors for missing fields, empty file lists, invalid HTTP(S) targets, negative lengths, and malformed lowercase SHA-256 digests.
- Shared file metadata stays persistence-neutral: ids are strings, KDF salt is serialized as Base64 text, and no bearer tokens, decryption keys, or other plaintext secrets are represented in `ShadowDrop.Shared`.

## Learnings — 2026-05-17T23:05:01.413+02:00

### Direct HTTP key cleanup on pre-transfer failures

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`

- Direct HTTP key material now flows through `DownloadFileService.WithDecodedDirectHttpKeyMaterialAsync`, which zeroes decoded `byte[]` buffers in a `finally` block unless ownership has already moved into `DirectHttpDecryptingStream`.
- This preserves existing malformed-key and invalid-request behavior while covering the previously unprotected path where blob opening or other pre-transfer work can throw after Base64 decode.
- Focused coverage lives in `DownloadFileServiceTests.WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroDecodedBytesWhenFailureOccursBeforeOwnershipTransfer`, alongside the existing stream-creation cleanup tests.

## Learnings — 2026-05-17T23:22:19.313+02:00

### Direct HTTP decrypting streams should retain invariant file keys

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`

- `DirectHttpDecryptingStream` now derives the file-scoped `ContentKey` exactly once during `CreateAsync`, transfers ownership into the stream, and disposes that retained key together with `_shareSecret` and `_kdfSalt`.
- Per-chunk decryption now reuses the retained `ContentKey` instead of recreating `ShareSecret` and repeating HKDF work inside `LoadNextChunkAsync`, which removes redundant allocations and derivations for large downloads.
- Regression coverage verifies the retained content key buffer is non-zero while the stream is active and is zeroed when the stream is disposed, alongside the existing failure-path cleanup assertions.

## 2026-05-15: Issue #4 Pre-Review Gate Cycle

**Session:** Scribe (2026-05-15T14:31:20.000Z)

Initial implementation of shared contracts completed. Parker (default reviewer) rejected due to missing FileMetadataContract coverage (round-trip / optional-field tests). Per lockout semantics, Eliot locked from next revision; Tara assigned to revise. Tara's revision passed Parker re-review.

- **Status:** Gate PASSED; PR #4 ready for user review
- **Decision merged:** Shared queue contracts + file metadata design finalized in `decisions.md`

## 2026-05-15: PR #10 Review-Note Follow-Up Implementation

**Session:** Scribe (2026-05-15T19:44:15.000Z)

Nate + Parker joint assessment of remaining Copilot review notes on PR #10 identified three critical fixes:
1. Missing queue-file length validation (bounds checking in parser validator, not constructor)
2. Insufficient target URL validation (must be absolute HTTP(S) only)
3. Incomplete XML documentation on shared contracts

Eliot implemented all three fixes on current branch:
- Modified `QueueFileParser.Validate` to check length bounds
- Tightened `QueueFileContract.target` URI validation to reject relative URLs
- Added XML docs to public API surface on all shared contract types
- Added targeted test cases for length bounds and URL validation

Parker re-reviewed post-implementation; all items verified and signed off.

Orchestration logs written. Decision "Shared Queue Contract Shape" documented in `decisions.md`.

- **Status:** Complete; PR #10 ready for merge
- **Next:** Awaiting user review

## Learnings — 2026-05-18T08:27:12.481+02:00

### Time-aware upload reservation validation

**Files touched:** `src/ShadowDrop.Api/Uploads/LiteDbUploadedFileMetadataRepository.cs`, `tests/ShadowDrop.Api.Tests/Uploads/UploadPersistenceServiceTests.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`

- Active upload reservations must be validated against the retention cutoff everywhere they are consumed, not only when issuing a later reservation that triggers lazy pruning.
- Centralizing reservation validity around the retention timestamp keeps `HasActiveReservationAsync` and `TryCompleteReservationAsync` consistent and prevents expired reservations from slipping through the upload completion path.
- Lazy pruning can remain in place for `ReserveFileIdAsync`, but stale reservations should also be deleted opportunistically when active-check or completion discovers that they are expired.

## Learnings — 2026-05-18T08:39:49.512+02:00

- Upload persistence now claims reservations before blob writes via repository-level `TryClaimReservationAsync`, and releases the claim on failure. That keeps invalid or concurrently reused file ids on the validation path instead of letting storage-layer collisions decide the outcome.
- LiteDB tracks in-flight claims explicitly (`IsClaimed`) so only one upload can advance a reservation, while expired unclaimed reservations are still pruned opportunistically during claim/complete/release checks.
- `DirectHttpDecryptingStream` secret cleanup must live in a shared sync/async disposal core so both `Dispose()` and `DisposeAsync()` zero the retained content key, share secret, KDF salt, and current plaintext chunk before the encrypted source stream is torn down.

## Learnings — 2026-05-18T11:19:54.273+02:00

### Range-aware download flow contracts

**Files touched:** `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`, `src/ShadowDrop.Api/Downloads/DownloadFileResolution.cs`, `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`, `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`

- The public download slice can keep a single endpoint while serving two resumable contracts: plaintext partial bodies for direct HTTP mode and JSON-wrapped encrypted chunk subsets for CLI-decrypt mode.
- Range parsing must happen on the authenticated path, not as an early endpoint rejection, so invalid-share, expired-share, and bearer-token failures stay behaviorally aligned with full downloads.
- For chunked encrypted blobs, direct-HTTP partial delivery only needs the first/last chunk span plus an in-stream trim window; there is no need to decrypt untouched chunks or buffer whole files.

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused
