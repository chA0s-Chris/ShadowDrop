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
