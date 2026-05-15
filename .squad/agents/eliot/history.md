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

## Learnings — 2026-05-15T16:17:18.120+02:00

### Shared queue and metadata contracts in ShadowDrop.Shared

**Files touched:** `src/ShadowDrop.Shared/Contracts/*.cs`, `src/ShadowDrop.Shared/Queue/*.cs`, `tests/ShadowDrop.Shared.Tests/Queue/QueueFileParserTests.cs`, `tests/ShadowDrop.Shared.Tests/Contracts/FileMetadataContractTests.cs`

- Shared protocol constants now live in `ShadowDrop.Contracts` and cover the direct-download header/query names, CLI config path segments, format versions, and the stable AES-256-GCM algorithm id.
- Queue contracts use nullable JSON-bound models plus an explicit `QueueFileParser.Validate` pass instead of constructor-only invariants; this keeps deserialization stable while still surfacing CLI-friendly validation errors for missing fields, empty file lists, invalid HTTP(S) targets, negative lengths, and malformed lowercase SHA-256 digests.
- Shared file metadata stays persistence-neutral: ids are strings, KDF salt is serialized as Base64 text, and no bearer tokens, decryption keys, or other plaintext secrets are represented in `ShadowDrop.Shared`.

## 2026-05-15: Issue #4 Pre-Review Gate Cycle

**Session:** Scribe (2026-05-15T14:31:20.000Z)

Initial implementation of shared contracts completed. Parker (default reviewer) rejected due to missing FileMetadataContract coverage (round-trip / optional-field tests). Per lockout semantics, Eliot locked from next revision; Tara assigned to revise. Tara's revision passed Parker re-review.

- **Status:** Gate PASSED; PR #4 ready for user review
- **Decision merged:** Shared queue contracts + file metadata design finalized in `decisions.md`
