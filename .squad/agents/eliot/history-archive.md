# Eliot — History Archive (Pre-2026-05-18)

## Initial Role & Context (2026-05-14)

- Seeded as Backend Dev for ShadowDrop MVP
- Stack: C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- Focus areas: encryption, upload/download flows, admin operations

## Cross-Agent Handoff: Crypto Implementation (2026-05-14)

Received crypto design from Nate (Issue #2 spike):
- Deterministic nonces from chunk index
- HKDF-SHA-256 key derivation
- 50-byte AAD binding
- `stackalloc` for AAD, `IDisposable` + `CryptographicOperations.ZeroMemory` on key types
- All AOT-safe (System.Security.Cryptography.HKDF, AesGcm)
- Parker targeting 26 test cases
- Branch: `squad/2-chunked-aes-gcm-crypto-spike`

## AdminTokenService & Program.cs Bug Fixes (2026-05-15)

**Files:** `src/ShadowDrop.Api/Program.cs`, `src/ShadowDrop.Api/Infrastructure/Security/AdminTokenService.cs`

- **HostAbortedException → OperationCanceledException**: Catch only `OperationCanceledException` to cover both `HostAbortedException` (inherited) and direct cancellations from `WebApplicationFactory`
- **LiteDB credential lookup**: Use `FindById` for ID-based queries, not `Query.EQ` on property names (LiteDB stores `Id` as `_id` internally)
- **LiteDB shared connection**: Change `ConnectionType.Direct` to `Shared` for concurrent test + host access (version 5.0.21 in Directory.Packages.props)
- **WebApplicationFactory startup failure**: When `Program.Main` catches exceptions, factory throws `InvalidOperationException("The server has not been started...")` not the original cause

## AdminTokenService Conditional Initialization & LiteDB Leak Fix (2026-05-15)

**Files:** `src/ShadowDrop.Api/CompositionRoot/DependencyInjection.cs`, `src/ShadowDrop.Api/CompositionRoot/Startup.cs`

- **Conditional registration**: Only add `AdminTokenService` to DI when `EnableAdminOperations == true`, avoiding forced bootstrap path when admin surface is disabled
- **LiteDB handle leak on constructor failure**: Try/catch around `EnsureBootstrapCredential`, call `_database.Dispose()` on exception to prevent file handle leak on first-boot failure
- **Test gap**: `ShadowDropOptionsBinding.ResolvePath` supports relative paths but lacks test coverage

## Crypto Key Retention Optimization (2026-05-15)

**Files:** `src/ShadowDrop.Api/Downloads/DirectHttpDecryptingStream.cs`

- Derive file-scoped `ContentKey` once during `CreateAsync` instead of per-chunk
- Retain `ContentKey` in stream, dispose together with `_shareSecret` and `_kdfSalt`
- Reuse retained key in `LoadNextChunkAsync` instead of recreating `ShareSecret` and repeating HKDF
- Removes redundant allocations and derivations for large downloads
- Regression coverage: verify retained content key is non-zero while active, zeroed on dispose

## Issue #4 Pre-Review Gate Cycle (2026-05-15)

- Initial shared contracts implementation completed
- Parker rejected due to missing `FileMetadataContract` coverage (round-trip / optional fields)
- Per lockout semantics: Eliot locked from next revision; Tara assigned to revise
- Tara's revision passed Parker re-review
- **Outcome:** Gate PASSED; PR #4 ready for user review

## PR #10 Review-Note Follow-Up Implementation (2026-05-15)

**Files:** Queue validators, URL validation, XML documentation

Implemented three critical fixes from Copilot review notes:
1. Added queue-file length validation in `QueueFileParser.Validate` (bounds checking)
2. Tightened `QueueFileContract.target` URI validation to reject relative URLs
3. Added XML docs to public API surface on shared contract types

Parker re-reviewed post-implementation; all items verified and signed off.
- **Status:** Complete; PR #10 ready for merge

## Time-Aware Upload Reservation Validation (2026-05-18 AM)

**Files:** `src/ShadowDrop.Api/Uploads/LiteDbUploadedFileMetadataRepository.cs`, tests

- Validate active upload reservations against retention cutoff everywhere they are consumed, not only at lazy pruning
- Centralize reservation validity around retention timestamp
- Keep `HasActiveReservationAsync` and `TryCompleteReservationAsync` consistent
- Prevent expired reservations from slipping through upload completion path

## Upload Persistence Claim & Release Flow (2026-05-18 AM)

**Files:** Upload persistence service and repository

- Claims reservations before blob writes via `TryClaimReservationAsync`
- Releases claim on failure; keeps invalid/concurrent file ids on validation path
- LiteDB tracks in-flight claims via `IsClaimed` flag
- Expired unclaimed reservations still pruned opportunistically
- Prevents storage-layer collisions from deciding outcome

## DirectHttpDecryptingStream Disposal Sync/Async Core (2026-05-18 AM)

**Files:** `src/ShadowDrop.Api/Downloads/DirectHttpDecryptingStream.cs`

- Unified disposal core for both `Dispose()` and `DisposeAsync()`
- Zeros retained content key, share secret, KDF salt, and current plaintext chunk
- Proper cleanup before encrypted source stream teardown

## Range-Aware Download Flow Contracts (2026-05-18 AM)

**Files:** Download endpoints, file resolution, file service, tests

- Public download slice keeps single endpoint for two resumable contracts:
  - Plaintext partial bodies for direct HTTP mode
  - JSON-wrapped encrypted chunk subsets for CLI-decrypt mode
- Range parsing on authenticated path, not early endpoint rejection
- For chunked encrypted blobs: only need first/last chunk span + in-stream trim window
- No need to decrypt untouched chunks or buffer whole files

## Streaming CLI Range JSON Without Buffering (2026-05-18 Late AM)

**Files:** Download file service, CLI resumable download contract tests

- Keep existing deterministic JSON contract while avoiding `byte[]` materialization
- Stream three segments: JSON prefix, on-the-fly Base64 of encrypted chunk span, JSON suffix
- Keep transport DTO on `ContractsJsonSerializerContext` for API and CLI
- Large-range regression coverage: assert lazy source reads before payload consumed
- Catches accidental reintroduction of eager buffering

