# Squad Decisions

## Active Decisions

- 2026-05-14: The initial ShadowDrop squad uses the user-chosen names Nate, Eliot, Sophie, Alec, Tara, and Parker, with Scribe and Ralph as built-in roles.
- 2026-05-14: ShadowDrop work is routed by specialization across lead, backend, CLI, security, platform, and testing rather than a generic pooled roster.

## Dependency & AOT Strategy

- 2026-05-14: Keep baseline dependency wiring aligned to deployment boundaries; make Native AOT support explicit in CLI project from first slice (Tara #1). Applied in Directory.Packages.props, ShadowDrop.Api.csproj, ShadowDrop.Cli.csproj. Ensures minimal API/CLI package surface, preserves shared-library boundary, enables `linux-x64` publish path for local and CI before feature code accumulates.

## Encryption & Cryptographic Design

- 2026-05-14 (Nate, Issue #2): Chunked AES-256-GCM crypto with deterministic nonces from chunk index, HKDF-SHA-256 per-file key derivation, sealed `ChunkEncryptionService` API. Test surfaces: 26 cases covering round-trip, range mapping, and tamper detection. AOT-compatible. See `.squad/decisions/inbox/nate-issue-2-crypto-design.md` for full details.

## Platform & Release Management

- 2026-05-14 (Tara, Issue #1): PR #5 targets `main` for foundational wiring (Directory.Packages.props, project refs, baseline builds). Rationale: no `dev` integration branch exists; stable baseline is essential before feature work.

## Team Policy

- 2026-05-14 (Copilot directive, Christian Flessa): Automatic commits allowed only for `./.squad/` changes. All commits must use Conventional Commits format; squad commits must use `docs(squad):` type prefix. Never push commits.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

### 2026-05-14T22:52:54.277+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Do not commit codebase changes unless explicitly asked; it is okay to commit squad-related changes inside `./.squad/`; do not push any commit to the remote; do not create a PR unless explicitly asked.
**Why:** User request — captured for team memory

### 2026-05-15T21:48:38.895+02:00: User directive — PR Review Resolution
**By:** Christian Flessa (via Copilot)
**What:** When addressing review findings from a PR, always add a small comment to each addressed conversation and then resolve it.
**Why:** User request — captured for team memory

### 2026-05-15T21:53:13.266+02:00: User directive — Plan Synchronization
**By:** Christian Flessa (via Copilot)
**What:** When implementing an issue that has a corresponding plan in `./ai-plans/`, always update the plan and check all acceptance criteria met by the implementation.
**Why:** User request — captured for team memory

## Review Gate & Process

### 2026-05-15T16:11:44.855+02:00: Pre-User Review Gate Policy
**By:** Nate (Lead)
**Area:** Process and Governance

Establish a mandatory internal review gate before any production changes reach the user for final approval.

- **Default review pair:** Nate (Lead) + Parker (Tester)
- **Security-sensitive escalation:** Alec (Security Engineer) joins for changes touching auth, tokens, crypto, secret handling, or permission boundaries.

**Rationale:** Fixed review pair ensures consistency, uses existing agent expertise without adding a dedicated role, reduces user time by catching issues early, and prevents security boundaries from slipping through.

**Implementation:** Review gate documented in `.squad/routing.md`; rejection follows strict lockout semantics with reassignment or escalation (never original author); Coordinator enforces on all future work.

## Test Coverage & Quality

### 2026-05-15: Walking Skeleton API Tests — Coverage Gaps Closed
**By:** Nate (Lead)

Four review gaps identified and closed in `ApiWalkingSkeletonTests.cs`:

1. **Public download enabled → 200**: Added `PublicDownloadEndpoint_ShouldReturn200_WhenPublicDownloadsAreEnabled` (enabled/200 path was untested).
2. **Failed-startup env cleanup**: Updated `Startup_BootstrapFailure_ShouldLeaveEnvironmentClean_ForSubsequentStartup` to directly assert `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` restoration.
3. **Relative-path cleanup junk**: Fixed silent bug where metadata path layout fell outside cleanup scope; now `{prefix}/metadata/shadowdrop.db`.
4. **Bootstrap startup failure**: `Startup_ShouldFail_WhenBootstrapAdminTokenIsMissingOnFirstBoot` asserts `InvalidOperationException` when token is missing.

**TestApiFactory signature extended:** `TestApiFactory(Boolean enableAdminOperations = true, Boolean enablePublicDownloads = true, Boolean withBootstrapToken = true)`.

### 2026-05-15T12:47:15.427+02:00: API Test Coverage Gaps — Addressed
**By:** Parker (Tester)

Four missing coverage scenarios added to `ApiWalkingSkeletonTests`:

| Scenario | Test |
|---|---|
| Wrong bearer token → 401 | `ManagementEndpoint_ShouldReturn401_ForWrongBearerToken` |
| Public downloads disabled → 404 | `PublicDownloadEndpoint_ShouldReturn404_WhenPublicDownloadsAreDisabled` |
| Upload route requires valid token | `UploadRoute_ShouldRequireValidAdminBearerToken` |
| Startup fails without bootstrap token | `Startup_ShouldFail_WhenBootstrapAdminTokenIsMissingOnFirstBoot` |

**WebApplicationFactory behavior note:** On .NET 10, startup exceptions are caught internally; `CreateClient()` throws `InvalidOperationException`; diagnostic message verified through code review and logged output.

## Cryptography & Buffer Management

### 2026-05-14T23:49:15.783+02:00: Share Salt Encapsulation & Derivation Cleanup
**By:** Alec (Security Engineer)

`FileEncryptionContext` must not generate a new salt as part of creation. Callers must pass the share-level KDF salt explicitly; `GenerateKdfSalt()` returns raw 32-byte only. `KdfSalt` now returns defensive copy (no mutations after construction); `ChunkEncryptionService.DeriveContentKey()` zeroes temporary buffers.

**Rationale:** Matches trust boundary design where one KDF salt is stored with share metadata and reused across all files; prevents silent violations from fresh-salt-per-invocation.

### 2026-05-15T00:01:13.117+02:00: Direct Guid Span Writes in Crypto Hot Paths
**By:** Tara (Platform Engineer)

When a hot path writes into a caller-provided `Span<byte>`, serialize `Guid` with `Guid.TryWriteBytes` instead of allocating temporary arrays via `ToByteArray()`.

**Context:** `src/ShadowDrop.Shared/Crypto/ChunkEncryptionService.cs` builds AAD and HKDF info blobs for every chunk/key derivation.

**Impact:** Keeps chunk encryption/decryption and key derivation closer to allocation-free behavior; preserves byte layout and matches stackalloc/span style already in use.

### 2026-05-15T00:16:55.489+02:00: Encrypted Chunk Buffer Ownership
**By:** Tara (Platform Engineer)

Treat `EncryptedChunk` like other crypto-adjacent buffers: copy inbound/publicly exposed `byte[]` at API boundary, keep internal `ReadOnlySpan<byte>` view for `ChunkEncryptionService`.

**Why:** Record-shaped API returning stored ciphertext array becomes silently mutable after validation; internal span keeps encrypt/decrypt on the no-extra-copy path the crypto code expects.

## Persistence & Storage

### 2026-05-15T12:58:42.780+02:00: LiteDB Shared Connection Mode for AdminTokenService
**By:** Eliot (Backend Engineer)

`AdminTokenService` opens LiteDB with `ConnectionType.Shared` (not default `Direct`).

**Reason:** In-process tests (WebApplicationFactory) open a second `LiteDatabase` connection to the same file while the service singleton is alive. `Direct` mode is exclusive, causing file-lock conflicts. `Shared` mode removes this constraint and makes the service safe for concurrent in-process access.

**Scope:** Any future singleton LiteDB repository read from tests during the same process lifetime should follow this pattern.

### 2026-05-15T13:34:42.045+02:00: AdminTokenService Conditional Initialization
**By:** Eliot (Backend Engineer)

`AdminTokenService` and its LiteDB handle + bootstrap credential check are only wired up when `ShadowDrop:ApiExposure:EnableAdminOperations` is `true`. When `false`, service is neither registered in DI nor resolved at startup.

**Previous issue:** Unconditional registration forced bootstrap env-var read, DB file open, and credential upsert to run regardless of whether admin endpoints were exposed — a leaky dependency requiring `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` and writable metadata path even on admin-disabled deployments.

**Side effect fixed:** `LiteDatabase` is now disposed in `AdminTokenService` constructor if `EnsureBootstrapCredential` throws, preventing file-handle leaks on first-boot failures (e.g. missing token).

**Test coverage gap:** `ShadowDropOptionsBinding.ResolvePath` handles relative paths but lacks dedicated test; recommended to add unit test for `BindAndValidate` with relative `LiteDbPath`.

## Security

### 2026-05-15: Bootstrap Token Normalization & Disposal Guard
**By:** Alec (Security Engineer)

Two surgical fixes applied to `AdminTokenService`:

**1. Bootstrap token whitespace normalization**  
`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` is now `.Trim()`-ed before hashing at bootstrap time, matching normalization in `TryReadBearerToken`. Without this, trailing newline or leading space in env var would produce permanent mismatch hash.

**Trust boundary:** Token comparison uses `CryptographicOperations.FixedTimeEquals`; normalization happens before hashing (not before comparison), so no timing channel introduced.

**2. Disposal guard extended to collection/index setup**  
The `try/catch { _database.Dispose(); throw; }` block now wraps `GetCollection` and `EnsureIndex` as well as `EnsureBootstrapCredential`. Previously, exception during index creation (schema conflict on existing DB) would leave handle open with no cleanup.

**No behavior change** for the happy path.

## Shared Contracts & Constants

### 2026-05-15T16:17:18.120+02:00: Shared Queue Contract Shape
**By:** Eliot (Backend Engineer)
**Area:** Shared API/CLI contracts

For issue #4, the shared queue format in `ShadowDrop.Shared` uses simple JSON-bound models plus an explicit parser/validator instead of baking hard validation into constructors or deserialization callbacks.

- Queue ids remain opaque `string` values.
- `target` must validate as an absolute HTTP or HTTPS URL.
- `plaintextSha256` is optional, but when present must be a 64-character lowercase hexadecimal digest.
- Shared file metadata is wire-oriented only and carries `kdfSalt` as Base64 text, never secrets or token-bearing values.

**Rationale:** Keeps the queue format stable across CLI and API, lets the CLI show precise validation errors, and avoids leaking server persistence concerns into `ShadowDrop.Shared`.
