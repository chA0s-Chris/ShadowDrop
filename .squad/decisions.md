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

## Upload API & Intake

### 2026-05-15T22:41:03.231+02:00: Upload API Plan Refinement — Accepted Suggestions Applied
**By:** Nate (Lead)
**Area:** Plan refinement and slice scoping

Applied four substantive refinements to `ai-plans/0011-upload-api-and-encrypted-file-intake.md` based on accepted review feedback:

**1. Upload Response Contract Tightened**  
Upload response now explicitly returns **only** file id and downstream-safe metadata (plaintext length, encrypted length, chunk count, encryption format version, algorithm id, chunk size) with **no secrets, derived keys, or internal state**. Prevents accidental exposure of `KdfSalt`, `ShadowDrop-Key`, or session tokens.

**2. Error Response Safety Requirement**  
Error responses must not expose secrets, key material, system paths, or internal validation details. Errors are generic HTTP status codes (400, 401, 413, 429) with minimal public message surface. Attackers use validation error messages to infer presence of files, guess metadata structure, or detect side-channel information leaks.

**3. Abuse Protection Gate**  
Upload endpoint enforces rate limiting or equivalent abuse protection to prevent high-volume upload spam. Without rate limits, a malicious client can fill storage or exhaust I/O with concurrent/sequential uploads.

**4. All-or-Nothing Upload Semantics**  
Failed uploads must use all-or-nothing / cleanup semantics. If validation fails after metadata is accepted, or if streaming fails mid-upload, all partially committed metadata and file content must be rolled back. No orphaned or partial records remain in the store.

**Scope Boundary Reinforced:** The plan explicitly does **not** expand into share creation, download setup, or authentication token refresh. Upload is intake-only. Later slices own the contracts for sharing and retrieval.

**Implementation Guidance:**
- Tests verify error responses do not leak file structure or secrets in HTTP headers, body, or status messages.
- Rate limiting can be achieved via middleware, endpoint-level guards, or token bucket.
- Cleanup/rollback on failed metadata commit should use database transactions or equivalent; test surfaces verify the invariant directly.

**Next Steps:** Implementation team (Eliot or assigned backend) owns the vertical slice. Acceptance criteria are binding. Review gate (Nate + Parker, escalate to Alec if secrets/auth touched) applies on PR.

## Upload Plan Clarifications — Metadata Validation & Cross-Layer Cleanup

### 2026-05-15T22:48:25+02:00: Upload Plan Clarifications
**By:** Nate (Lead)  
**Request:** Christian Flessa  
**Area:** `ai-plans/0011-upload-api-and-encrypted-file-intake.md` Technical Details section

Two clarifications tighten the upload implementation contract:

**1. Metadata Validation Before Stream Consumption**  
Reject malformed envelope metadata (invalid lengths, inconsistent format, missing required fields) **before starting to consume the request body stream**. Metadata parsing is synchronous and complete before any I/O begins; prevents wasting bandwidth on format errors and establishes a clear validation gate.

**2. Cross-Layer Cleanup Semantics**  
All-or-nothing rollback must span **every persistence layer** in the upload path. If blob content is written before metadata commit succeeds, or if streaming fails mid-upload, that content must be deleted and no orphaned state may remain (database, filesystem, or other persistence backend). Use database transactions with deferred writes or multi-phase commit; test surfaces verify the invariant directly by simulating failure at each boundary.

**Scope Boundary:** Neither clarification expands into share creation, download setup, token refresh, or other downstream concerns. Upload remains intake-only; implementation team owns the vertical slice with binding acceptance criteria.

**Next Steps:** Implementation team uses clarifications as validation checklist during code review. Reviewer gate (Nate + Parker, escalate to Alec if secrets/auth touched) applies on PR. Test surfaces must verify both constraints directly.

### 2026-05-16T08:44:15.662+02:00: User directive — Defer Integrity Verification
**By:** Christian Flessa (via Copilot)
**What:** Defer integrity verification in plan 0012 rather than adding it to this slice.
**Why:** User request — captured for team memory

### 2026-05-16T09:15:58.247+02:00: User directive — Model Availability
**By:** Christian Flessa (via Copilot)
**What:** `claude-opus-4.5` and `gemini-3-pro-preview` are no longer available.
**Why:** User request — captured for team memory

### 2026-05-16T09:21:58.062+02:00: User directive — Available Models & Cost Multipliers
**By:** Christian Flessa (via Copilot)
**What:** Available models are `gpt-5.5` (7.5x), `gpt-5.4` (1x), `gpt-5.3-codex` (1x), `gpt-5.2-codex` (1x), `gpt-5.4-mini` (0.33x), `gpt-5-mini` (free), `gpt-4.1` (free), `claude-sonnet-4.5` (1x), `claude-sonnet-4.6` (1x), `claude-haiku-4.5` (0.33x), and `claude-opus-4.7` (15x). Ask before using `claude-opus-4.7`. For tasks that previously would have used Claude Opus 4.5 or 4.6, use `gpt-5.5` instead.
**Why:** User request — captured for team memory

## Share Creation, Expiration & Hashed Bearer Tokens

### 2026-05-16T09:28:05.383+02:00: Security Review — Plan 0013 Approved with Surgical Clarifications
**By:** Alec (Security Engineer)
**Area:** Plan 0013 — share creation, expiration, hashed bearer tokens

Plan 0013 is **sound in trust architecture**. Token handling follows established pattern (hash-on-store, FixedTimeEquals on validate), expiration semantics explicit, direct-HTTP mode opt-in. Five surgical clarifications locked down secret lifecycle, HTTP boundaries, expiration semantics, schema initialization, and token-mode combinations:

1. **Bearer-Token Entropy Requirement:** Both share tokens and download bearer tokens must use `RandomNumberGenerator.GetBytes()` with minimum 32 bytes (256 bits) entropy. Implementers cannot substitute weaker RNGs or reduce byte length.

2. **Plaintext-Token Lifetime Boundary:** Plaintext tokens returned in creation response are volatile secrets existing only during request handling and in HTTP response. After transmission, only hash persists. Do not log, cache, or commit plaintext to server-side logs, metrics, or audit trail; log token hash or metadata fingerprint instead.

3. **Download Bearer-Token Expiration Atomicity:** Expiration checked at validation time only during download request. Shares do not actively revoke download tokens; expired token simply rejected on next use. Soft expiration (checked at use) is simpler and requires no cleanup jobs.

4. **Revocation & Cleanup State Field Semantics:** `optional_revocation_timestamp` and `cleanup_state` must be explicitly initialized at share creation (not left null): `optional_revocation_timestamp = NULL` (not revoked), `cleanup_state = "PENDING"` (shares start unrevoked). Future-proofing for revocation/cleanup endpoints without implementing them here.

5. **Direct-HTTP & Bearer-Token Combination Validation:** Explicitly reject invalid combinations: forbidden are (direct-HTTP + bearer-token requirement) and (direct-HTTP + separate-key mode simultaneously). Allowed: separate-key mode + optional bearer token, direct-HTTP mode + no bearer token. Test coverage: at least three rejected combinations.

**Review Gate Implications:**
- Default pair: Nate (Lead) + Parker (Tester)
- Alec escalation required (token generation, hashing, confidentiality)
- Criteria: atomic persistence, token entropy, plaintext handling, error response safety, invalid-combination validation

**Next Steps:** Implementation team (Eliot or assigned backend) owns vertical slice with binding acceptance criteria.

### 2026-05-16T09:31:13.101+02:00: Plan 0013 — All Review Suggestions Applied
**By:** Nate (Lead)
**Request:** Christian Flessa ("Please apply all suggestions")
**Artifact:** `ai-plans/0013-share-creation-expiration-and-hashed-bearer-tokens.md`

Eight substantive refinements applied; all now formalized as binding acceptance criteria:

**Material Changes:**
1. **Token Entropy Floor Specified** — 256 bits (32 bytes) minimum for share tokens and optional download bearer tokens. Binding criterion.
2. **Plaintext Token Confidentiality Enforced** — Returned only once at creation; never persisted, logged, or server-side recorded. Three new acceptance criteria spanning hash persistence, plaintext handling, log confidentiality.
3. **Expiration Validation Deferred** — Expiration checking belongs entirely to later slices. This slice only persists expiration timestamps as metadata.
4. **Revocation and Cleanup Fields Initialized** — Both fields persisted at share creation time (revocation_timestamp = NULL, cleanup_state = false) without implementing revocation/cleanup endpoints.
5. **Invalid Mode/Token Combinations Spelled Out** — Direct-HTTP + bearer token, and separate-key without bearer configuration explicitly rejected with clear rationale.
6. **Error Response Safety Tightened** — Generic HTTP 400 with minimal public message; never expose constraint logic or token-shape details.
7. **Atomic Persistence Required** — Share metadata, file entries, token hashes persist atomically; partial failures trigger full rollback.
8. **Scope Boundaries Reinforced** — Does not implement revocation endpoints, cleanup jobs, download endpoint, or download token validation. All belong to later slices.

**Acceptance Criteria Status:**
- Explicit, binding, testable, focused
- No scope creep into download, validation, or cleanup endpoints
- Plan ready for implementation
- Review gate applies on PR submission
