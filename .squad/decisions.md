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
# Plan 0013 — Cleanup State Representation Clarification

**Date:** 2026-05-16T09:49:47+02:00  
**Author:** Nate (Lead)  
**Context:** Polish pass on `ai-plans/0013-share-creation-expiration-and-hashed-bearer-tokens.md`

## Decision

Resolve ambiguity in cleanup state representation by explicitly specifying a **named enum state** rather than a boolean flag.

### Change Rationale

The original plan used conflicting terminology:
- **Line 244 (decisions.md):** `cleanup_state = "PENDING"` (string/enum state)
- **Line 266 (decisions.md):** `cleanup_state = false` (boolean flag)

This ambiguity creates implementation risk: implementers could conflate a boolean flag with a state enum, leading to inflexible schema or missed extensibility.

### Binding Specification

**Cleanup state is initialized as a discrete named enum value, not a boolean:**
- Initialized to `"PENDING"` at share creation (not `false`)
- Represents a discrete state in cleanup job orchestration
- Enables future extensibility (e.g., `"PENDING"` → `"IN_PROGRESS"` → `"COMPLETED"`)
- Persisted in metadata alongside revocation timestamp

### Implementation Boundary

This slice persists the initial state (`"PENDING"`) only. Revocation endpoints, cleanup job implementations, and state-transition logic belong to later slices.

### Acceptance Criteria Impact

Acceptance criterion line 26 (now reinforced):
- ✓ Revocation timestamp (nullable, `NULL` until revoked)
- ✓ Cleanup state (named enum, initialized to `"PENDING"`, not a boolean default)

Plan remains **implementation-ready** with this clarification.
# Plan 0014 — Download Endpoint Clarifications

**Session:** 2026-05-16T16:52:54.613+02:00 (Nate)

## Summary

Applied two binding clarifications to `ai-plans/0014-basic-download-endpoint.md` per Christian Flessa review:

1. **Expiration Enforcement Model:** Clarified that expiration is a soft check performed at download-time validation. Acceptance criterion now states: "The endpoint denies expired shares by validating `expiration_timestamp < now` at download time. Expiration is soft (checked on each request); no cleanup jobs or active background revocation required." This aligns with pattern established in Plan 0013 (share creation) and prevents scope creep into revocation/cleanup endpoints, which belong to later slices.

2. **Audit & Logging Metadata Boundaries:** Strengthened logging safety requirement to explicitly allow safe identifiers and outcomes while forbidding sensitive material. New guidance: "Audit and logging metadata may include safe identifiers (share id, file id, request outcome) and high-level success/failure results, but must not include token hashes, Authorization header content, ShadowDrop-Key header content, or plaintext key material." Prevents accidental trace leaks while clarifying what auditing is acceptable.

## Impact

- Expiration clarification closes ambiguity on soft vs. hard expiration and aligns download validation with share-creation metadata model.
- Logging clarification provides explicit allowlist (safe identifiers, outcomes) rather than just denylist (keys, tokens), reducing implementer uncertainty and preventing trace exposure.
- Both clarifications tighten the contract without expanding slice scope. Plan remains focused on authorization and streaming.

## Status

Plan is now **implementation-ready**. Acceptance criteria are binding and explicit. Review gate (Nate + Parker, escalate Alec for auth/tokens/security) applies on PR submission.
# Plan 0015 Clarifications: Range Requests & Resumable Downloads

**Date:** 2026-05-16T21:18:02+02:00  
**Author:** Nate (Lead)  
**Status:** Ready for team review and implementation

## Changes Applied

Eight clarifications applied to `ai-plans/0015-range-requests-and-resumable-downloads.md` per Christian Flessa request:

### 1. Explicit Security Criterion (Acceptance Criteria)
Added binding acceptance criterion: **Range requests enforce the same share-token, optional download bearer-token, and expiration checks as full downloads.** Prevents security bypass via partial-content responses on invalid credentials or expired tokens.

### 2. Security Test Criterion (Acceptance Criteria)
Added binding acceptance criterion: **Security-focused tests prove that invalid, expired, or unauthorized range requests are rejected and do not return partial content.** Enforces that authentication/expiration failures return appropriate error responses (401, 403, 410) with no partial content, not 206.

### 3. Plaintext-Range-to-Chunk-Span Mapping (Technical Details)
Added concrete six-step mapping algorithm:
- Chunk index calculation from plaintext range bounds
- Plain boundary tracking within chunks
- Encrypted chunk span streaming (no full-file materialization)
- Decryption window trimming for Direct-HTTP mode
- Encrypted subset return for CLI mode
- Preserves resumability without full-file transfers

### 4. HTTP 206 Response Contract (Technical Details)
Formalized exact header contract for Direct-HTTP mode:
- `206 Partial Content` status
- `Content-Range: bytes {start}-{end-1}/{total-plaintext-size}` with exact semantics
- `Content-Length: {end - start}` (partial response size only)
- `Accept-Ranges: bytes` (signals server support)
- Response body contains only `[start, end)` plaintext bytes

### 5. Separated Direct-HTTP vs CLI Semantics (Technical Details)
Distinguished two modes explicitly:
- **Direct-HTTP range mode:** Server decrypts internally, returns HTTP 206 with plaintext; standard `curl -C -` compatibility.
- **CLI encrypted-subset mode:** Server returns encrypted chunks (no HTTP 206); CLI decrypts locally; chunk boundaries enable resumability.
Each mode enforces full authentication and expiration validation at request time.

### 6. Tightened Chunk-Span Streaming (Technical Details)
Reinforced that local blob backend must:
- Open stream and read only required chunk span
- Avoid materializing whole files or all chunks upfront
- Prove mid-chunk and multi-chunk requests produce correct plaintext bytes
- Enforce authentication and expiration the same way as full-file downloads

### 7. Structure & Scope Preserved
Plan structure remains unchanged: Rationale, Acceptance Criteria (now with two new security criteria), and Technical Details (now with concrete mappings and header contracts).  
No slice expansion: download endpoint is still in-scope; share creation, token refresh, revocation belong to later slices.

### 8. Implementation-Ready Status
All acceptance criteria are now binding and testable:
- Security enforcement (token/expiration checks on range requests)
- Concrete byte-mapping algorithm
- Exact HTTP 206 response contract
- Separable Direct-HTTP and CLI resumable modes
- Streaming-oriented blob backend

## Rationale

These clarifications remove ambiguity around:
- **Security:** Explicit criteria ensure range requests do not bypass auth/expiration checks
- **Compatibility:** Concrete HTTP 206 contract ensures curl/wget resumption works
- **Implementation guidance:** Plaintext-to-chunk mapping removes guesswork; CLI mode is explicitly non-206 to avoid HTTP semantics confusion
- **Scope boundary:** Separating Direct-HTTP (206) from CLI encrypted-subset (no 206) prevents accidental feature creep

## Next Steps

Plan is now implementation-ready. Backend team (Eliot) and testing team (Parker, Tara) can use the concrete mapping algorithm and HTTP contract as implementation targets. Review gate applies on PR.
# Plan 0015: CLI Contract Lock-Down & Error Handling Polish

**Date:** 2026-05-16T21:22:43.120+02:00  
**By:** Nate (Lead)  
**Area:** CLI Integration & Security  

## Summary

Updated `ai-plans/0015-range-requests-and-resumable-downloads.md` to lock down the CLI resumable-download contract and apply explicit non-leaky error handling.

## Changes

### 1. CLI Resumable-Download Contract (Locked)

**Updated acceptance criterion:** "CLI resumable-download contract locked: The CLI receives a deterministic response shape that includes encrypted chunk data, chunk span metadata (first/last chunk index), plaintext range boundaries, and total file size—sufficient for CLI to seek within chunks, decrypt locally, resume on interrupt, and avoid full-file transfer."

**New Technical Details section:** "CLI resumable encrypted-subset mode (contract locked)" now specifies the exact JSON response shape the implementation must use:

```json
{
  "firstChunkIndex": 2,
  "lastChunkIndex": 5,
  "encryptedPayload": "<base64-encoded concatenated chunks>",
  "requestedRange": { "start": 131072, "end": 262144 },
  "totalPlaintextSize": 1048576,
  "chunkSize": 65536,
  "finalChunkPlaintextLength": 32768
}
```

**Rationale:**
- Previously, the plan said "wrapped in a structured response (e.g., JSON with chunk metadata and ciphertext)" — vague and unimplementable.
- New contract is explicit: which fields are required, their meanings, and why each is needed for CLI resumability and chunk-level seeking.
- Implementation team now has a binding, unambiguous shape to code against.
- No design changes to download flow; contract is derived from existing architecture.

### 2. Range-Request Error Handling (Non-Leaky)

**Updated acceptance criterion:** "Range-request error handling is explicitly non-leaky: Invalid ranges, invalid/expired tokens, and authorization failures all use generic HTTP status codes (400, 401, 403) without exposing range details, token validation logic, or file size information in error responses."

**New Technical Details section:** "Range-Request Error Handling (Non-Leaky)" details all six error scenarios:

1. **Invalid or unsatisfiable range (HTTP 416):** No range hints; no file-size leak.
2. **Invalid or missing share token (HTTP 401):** No differentiation between missing vs. invalid vs. revoked.
3. **Expired share (HTTP 401):** Same generic message as token errors; no time info.
4. **Unauthorized bearer token (HTTP 403):** No differentiation between absent and invalid.
5. **Direct-HTTP: Invalid range format (HTTP 400):** No reflection of malformed input.
6. **CLI: Missing or invalid parameters (HTTP 400):** No parameter names or type hints.

**Omitted from all error responses:**
- File size (plaintext or encrypted)
- Chunk configuration details
- Token validation state or reason
- Time information
- Range validity hints

**Rationale:**
- Previous plan said errors should not leak, but was not explicit about *which* errors leak *what* information.
- New section provides a binding checklist for implementation and testing.
- Prevents accidental information disclosure under time pressure.
- Aligns with earlier upload-plan and share-creation-plan security refinements (consistent security posture across slices).
- Each error scenario is concrete and testable.

## Scope Boundaries

**Preserved (no scope expansion):**
- Acceptance criteria count unchanged (still covers all mandatory behaviors).
- Chunk-to-range mapping logic identical.
- Direct-HTTP 206 response contract unchanged.
- Storage streaming and memory constraints unchanged.
- Test surface (full-file, aligned ranges, mid-chunk, multi-chunk, unsatisfiable) unchanged.

**Refined (no new implementation, clarity only):**
- CLI response shape pinned from "structured response (e.g., JSON)" to an exact schema.
- Error handling pinned from "don't leak" to explicit per-error-type rules and omission lists.

## Implementation Readiness

Plan is **now implementation-ready:**
- CLI contract is explicit enough for backend and frontend (CLI) teams to implement independently.
- Error handling is explicit enough for test-driven development (each scenario listed with expected status and behavior).
- Scope is tight; no scope creep; no new features or system interactions.
- Acceptance criteria are all checkable and measurable.
- Review gate (Nate + Parker, escalate Alec for token/auth/security) applies on PR.
# Plan 0015 Polish Items: Accept-Ranges Consistency & Token Validation Pattern Reuse

**Date:** 2026-05-16T21:26:32+02:00  
**Lead:** Nate  
**Requested by:** Christian Flessa

## Summary

Added two clarifying polish notes to `ai-plans/0015-range-requests-and-resumable-downloads.md` to strengthen the plan's authentication and header-consistency contracts:

### 1. Accept-Ranges Header Scope Clarification

**Location:** HTTP 206 Response Contract section  
**Change:** Added polish note that `Accept-Ranges: bytes` must be returned consistently:
- On full-file (non-206) responses
- On all HTTP 4xx/5xx error responses to full-file requests

**Rationale:** Clients discover range-request capability via this header. Returning it only on 206 responses creates an incomplete signal. Consistent advertising allows clients to assume resumable-download support without an extra OPTIONS call, improving usability for standard tools like `curl -C -`.

### 2. Bearer-Token Validation Pattern Reuse

**Location:** Technical Details section, end of general guidance  
**Change:** Added polish note clarifying that optional download bearer-token validation must:
- Hash the bearer token using the same algorithm as share-creation
- Compare using fixed-time string comparison
- Reuse the existing stored-hash/fixed-time-compare pattern from share-creation

**Rationale:** Establishes explicit security guidance to prevent token-timing attacks and accidental timing-leak introduction during range-request implementation. Ensures range requests apply identical token-validation gates as full-file downloads. Reduces implementation surface by directing team to existing established pattern rather than inventing a new validation approach.

## Acceptance

Both polish notes:
- Preserve plan structure and tone
- Do not expand slice scope
- Apply surgical edits to existing sections
- Formalize team-wide implementation guidance on auth and header consistency

**Status:** Ready for implementation team reference during PR review gate.
# CLI Upload Command (Plan 0016) — Five Clarifications Applied

**Date:** 2026-05-16T21:52:25.986+02:00  
**By:** Nate (Lead)  
**Area:** `ai-plans/0016-cli-upload-command.md` — Plan refinement and acceptance criteria tightening

## Summary

Five substantive clarifications have been folded into the CLI upload command plan to eliminate ambiguity and establish explicit security and contract guarantees. All changes preserve the slice scope (intake-only, non-interactive, client-side encryption) while tightening implementation contracts.

## Clarifications Applied

### 1. Explicit Configuration Precedence

**Added to acceptance criteria & Technical Details:**
- Config resolution order is binding: **flags > environment variables > config file**.
- Rationale: Users cannot override stale config without understanding precedence; ambiguity creates footgun scenarios.
- Test surface: Three configuration sources tested in isolation and in combination to verify precedence holds.

### 2. Admin Token Handling Guidance

**Added to Technical Details → Configuration Precedence & Token Handling:**
- Trim whitespace before use (matching server-side normalization in `AdminTokenService`).
- Never log, cache, or persist plaintext token in stdout, stderr, debug output, or telemetry.
- Explicitly document the visibility risk: tokens passed via CLI flags may be visible to process inspection (e.g., `ps`, Process Explorer).
- Recommend users prefer environment variables or config file for sensitive deployments.

**Trust boundary:** Token handling follows established pattern: trim early, hash/compare using fixed-time operations, no plaintext persistence.

### 3. Concrete CLI Output Contract

**Added to Technical Details → CLI Output Contract and acceptance criteria:**
- **stdout:** Successful file ids only, one per line. No diagnostics or progress.
- **stderr:** All diagnostic messages, validation errors, warnings, and progress.
- **Exit code:** 0 if all files succeed; non-zero on any failure (file read, validation, API error, network failure, or partial upload failure).
- Rationale: Makes CLI script-friendly and unambiguous in automated workflows.
- Test surface: Verify stdout contains only ids, stderr contains diagnostics, exit codes match outcomes.

### 4. Error Message Surface Rules

**Added to Technical Details → Error Messages & Security:**
- Error messages must be **clear but generic**.
- Never expose: file paths, server URLs, token values, encryption key material, internal server details, or stack traces.
- Examples provided of unsafe vs. safe error messages.
- Rationale: Attackers use validation error messages to infer presence of files, guess metadata structure, or detect side-channel information leaks.

### 5. Stronger Streaming Wording

**Added to Technical Details → Encryption & Streaming and acceptance criteria:**
- Use **bounded buffers** during encryption and upload; do not accumulate full plaintext in memory.
- Specify fixed-size chunk buffers (e.g., 1 MB) to read plaintext, encrypt chunk-by-chunk, and write encrypted bytes directly to HTTP stream.
- Deallocate buffers between chunks.
- Rationale: Matches existing chunked-AES-GCM architecture and prevents plaintext accumulation footgun in CLI context.
- Acceptance criterion updated to binding: "streams encrypted content using bounded buffers, never accumulating full plaintext in memory during encryption or upload."

## Scope & Boundaries

No changes to slice scope. CLI remains:
- **Intake-only:** No share creation, download setup, or token refresh.
- **Non-interactive:** No wizard prompts or user input beyond file paths and config resolution.
- **Client-side encryption:** Generates share secret, derives per-file keys, streams encrypted bytes.
- **Native AOT compatible:** Explicit JSON contracts, no reflection-heavy conveniences.

## Implementation Guidance

1. **Configuration:** Parse flags first, fall back to env vars, fall back to config file; never mix sources without explicit precedence.
2. **Token safety:** Trim token at parse time; never log plaintext; document CLI flag visibility risk in help text.
3. **Output:** Strict stdout/stderr separation; exit codes binding.
4. **Errors:** Use generic HTTP-style codes (400, 401, 413, 429) where applicable; make error messages safe to log or expose to end users.
5. **Streaming:** Implement with 1 MB chunk buffers (or similar bounded size); verify no full plaintext buffer in memory snapshots during testing.

## Review Gate

- **Default pair:** Nate (Lead) + Parker (Tester)
- **Escalation:** Alec (Security Engineer) joins for token handling, encryption, error surface, and CLI security review.
- **Test coverage:** Binding acceptance criteria require tests for all five clarifications.

## Next Steps

Implementation team (CLI ownership, likely Sophie or assigned) uses these clarifications as validation checklist during code review. All acceptance criteria are binding. Reviewer gate applies on PR submission.
# Plan 0016: CLI Upload Command — Final Polish Applied

**Date:** 2026-05-16T22:00:47+02:00  
**Owner:** Nate  
**Request From:** chA0s-Chris

## Changes Applied

Refined `ai-plans/0016-cli-upload-command.md` with two substantive polishing items:

### 1. Token Terminology Clarification

Renamed all occurrences of "admin token" to **"upload authorization token"** throughout the plan to precisely reflect that this is a narrower, intake-specific credential rather than a broad administrative token.

**Locations updated:**
- Acceptance criterion: "upload authorization token" in configuration resolution
- Validation criterion: "upload authorization token" in error handling
- Technical Details: "Upload authorization token" in Configuration Precedence section
- Flag name example: `--upload-token` (changed from `--admin-token`)

**Rationale:** The terminology change makes the scope of the credential explicit—this token grants **upload/intake rights only**, not admin-level control. Prevents confusion with broader authorization models and aligns with principle of least privilege. Improves clarity for implementation and end-users.

### 2. Upload-Only Scope Clarification

Enhanced the Encryption & Streaming section with explicit statement: **"This command performs upload intake only and does not create shares or attach files to existing shares"**.

**Location updated:**
- Encryption & Streaming section, final bullet point

**Rationale:** Clarifies slice boundary and prevents scope creep during implementation. Reinforces that this is a pure intake workflow with no share-creation or attachment logic. Consistent with established pattern from earlier upload plan refinements (plans 0011–0013).

## Impact

- **Scope:** No expansion; terminology and clarity only.
- **Acceptance Criteria:** Remain binding and unchanged in substance.
- **Implementation Guidance:** Token handling now reads as narrower intake authorization; scope boundary reaffirmed.
- **Team Alignment:** Both changes are cosmetic refinements that improve precision without broadening responsibilities.

## Decision Status

**Accepted by:** Christian Flessa  
**Applied:** Yes  
**Ready for Implementation:** Yes
# Plan 0016: CLI Upload Command — Final Polishing (Last Two Suggestions)

**Date:** 2026-05-16T22:09:27+02:00  
**Owner:** Nate  
**Request From:** chA0s-Chris

## Changes Applied

Refined `ai-plans/0016-cli-upload-command.md` with two final substantive polishing suggestions to eliminate remaining ambiguity for implementation team:

### 1. HTTP Status & Retry Behavior Clarification

**Added as new subsection in Technical Details (after "Error Messages & Security"):**

Distinguishes between permanent and transient failure modes so implementers know which HTTP responses warrant retry vs. immediate failure:

- **Immediate failure (no retry):** HTTP 401 (unauthorized token), 403 (permission denied), 400 (malformed request), 413 (payload too large)
- **Transient/retriable:** HTTP 429 (rate limit), 503 (unavailable), network timeouts, connection errors
- **Partial failure:** Multi-file uploads do not auto-retry failed files; user must re-run with failed file paths
- **Per-file atomicity:** Individual file uploads are all-or-nothing; no resume on mid-transfer interruption

**Rationale:** Prevents retry storms on auth failures (401), clarifies when exponential backoff is appropriate, aligns with zero-knowledge upload architecture where secrets never reach the server. Implementer can code this decisively without ambiguity.

### 2. Share-Level Secret Lifecycle Clarification

**Added as new subsection in Technical Details (after "Encryption & Streaming"):**

Makes explicit the ownership and lifecycle of the share-level secret so downstream callers understand how the CLI manages it:

- **Generation & ownership:** CLI generates 256-bit secret client-side; caller manages its lifecycle (no CLI persistence)
- **Emission:** Secret is **never** emitted by default; only if user opts in via flag (e.g., `--output-secret`)
- **Server-side:** Plaintext secret never reaches the server; only encrypted content and non-secret metadata uploaded
- **Downstream integration:** Separate share-creation command accepts secret + file ids as inputs
- **Out-of-band management:** Users may manage secret via secure key store, HSM, or secrets manager

**Rationale:** Reinforces zero-knowledge architecture, keeps intake slice narrow, prevents accidental server-side secret storage. Implementation team knows exactly what the CLI owns vs. what the caller must manage.

## Impact

- **Scope:** No expansion; operational clarity and retry semantics only
- **Acceptance Criteria:** Binding criteria now include retry behavior tests and secret lifecycle validation
- **Architecture:** Reinforces zero-knowledge pattern; integrates cleanly with later share-creation and download slices
- **Team alignment:** Both clarifications prevent implementation footguns and reduce review friction

## Decision Status

**Requested by:** Christian Flessa  
**Applied:** Yes  
**Test coverage required:** 
- Retry behavior: Verify immediate-fail status codes exit with non-zero, retriable codes permit retry logic
- Secret lifecycle: Verify plaintext secret is not logged/persisted by default, only emitted on explicit flag

**Ready for Implementation:** Yes

## Next Steps

Implementation team uses these two clarifications as validation checklist. All acceptance criteria (including new retry and secret-lifecycle behavior) are binding for PR review gate.
# Plan 0017 Clarifications: CLI Download Command and Queue Processing

**Date:** 2026-05-16  
**Context:** Edited plan document to resolve ambiguities identified during team review.

## Decisions Made

### 1. Share Key Input Contract: Dual Path, No Prompting
**Decision:** Share keys are provided via `--share-key <key>` (CLI argument) or `--share-key-file <path>` (file), with CLI argument taking precedence.
- **Rationale:** Gives callers flexibility without introducing interactive prompts in a CLI tool designed for automation and scripting.
- **Non-interactive requirement:** If the share key is missing, the command exits with code 1 immediately. This prevents unexpected hangs or failures in batch workflows.

### 2. Bearer Token: CLI Argument Only, No Environment/Config Fallback
**Decision:** Bearer tokens are sourced exclusively from `--bearer-token <token>`. Environment variables and config files are not consulted.
- **Rationale:** Explicit, predictable behavior for a security-sensitive credential. Implementers know exactly where tokens come from, reducing the risk of leaking tokens via config files or unintended env var exposure.
- **Practical:** Callers must explicitly pass bearer tokens each time or script them in, making it clear that auth is happening.

### 3. Output and Exit Code Behavior
**Decision:** 
- **Direct downloads:** Content to stdout, errors/status to stderr, exit 0/1.
- **Queue processing:** Summary report to stderr, exit 0 if all succeed, exit 1 if any fails. Partial failures do not stop processing.
- **Rationale:** Allows easy piping (stdout for content), clear diagnostics (stderr), and predictable batch error handling (continue on failure, report summary).

### 4. Secrets Security Handling
**Decision:** Share keys and bearer tokens must never be written to queue files, logs, or stderr output.
- **Secret disposal:** Where C# data structures support it (e.g., `SecureString`), secrets are cleared/zeroed after use.
- **Rationale:** Prevents accidental exposure of sensitive credentials in audit trails, backups, or queue file diffs.

### 5. Scope Boundary: No Resume Support in This Slice
**Decision:** This slice does not include range-request resume functionality. The design explicitly structures the download pipeline to allow resume support to be added cleanly in a future slice.
- **Rationale:** Keeps scope focused on core download and queue processing. Resume logic can be layered on top without redesign.

## Implementation Guidance
- Use `System.CommandLine` for robust option parsing and validation.
- Validate the full queue before beginning any downloads.
- Report per-file status in the summary to help callers identify which entries succeeded and which failed.
- Consider using `System.Security.Cryptography` or `System.Security.SecureString` for secret cleanup where appropriate.
# Plan 0017 Final Polish — Decisions

## Decision 1: Explicit Queue Entry Contract
Added a new **Queue Entry Contract** section that formally documents the exact fields expected in each queue entry:
- `serverUrl`, `shareId`, `outputPath`

This section explicitly states that secrets are **never stored in queue entries**—all secrets come from CLI arguments only. This eliminates any ambiguity and ensures downstream consumers of this plan understand the security boundary clearly.

## Decision 2: Upfront Validation Abort
Clarified that queue validation happens **before any downloads begin**. If validation fails, the command exits with code 1 immediately—no partial downloads, no downloads at all. This is a clear architectural contract that prevents partial failures from queue format issues.

## Decision 3: No Secret Emission in Diagnostics
Tightened the secrets handling language from "masked values if needed for diagnostics" to an absolute rule: **secrets are never emitted in diagnostics—not even partially masked or redacted**. This removes any temptation to add "helpful" masked output and ensures consistent, strict secret handling across all code paths.

## Impact
These three clarifications strengthen the security contract and reduce the surface area for implementation mistakes. Teams building on this plan will have clear, unambiguous requirements for queue structure, validation flow, and secret handling.
# Plan 0018: Interactive Spectre Console UX — Clarifications

**Decision Maker:** Nate (Lead)  
**Date:** 2026-05-16  
**Status:** Applied to plan

---

## Summary

Applied six focused clarifications to plan 0018 to ensure implementers understand the scope, integration boundaries, security requirements, and testing expectations for the interactive guided CLI layer.

---

## Clarifications Applied

### 1. Interactive Mode Invocation (System.CommandLine Unambiguity)

**Decision:** Interactive mode must be invoked with an explicit `--interactive` flag or equivalent subcommand structure.

**Rationale:** Without a clear invocation boundary, implementers might accidentally blend interactive and non-interactive parsing, creating ambiguous argument-handling behavior. An explicit flag makes the mode orthogonal to the base System.CommandLine parser and allows easy downstream documentation.

**Implementation note:** Suggest either:
- `shadowdrop upload --interactive` (flag-style)
- `shadowdrop --interactive upload` (top-level prefix)

Choose one pattern and document it clearly in the implementation.

---

### 2. Dependency on Underlying Non-Interactive Flows

**Decision:** Rationale and Technical Details now explicitly state that the interactive layer is a **UI wrapper only**, delegating all business logic to existing/concurrent plans 0016 (upload), 0013 (share creation), and 0017 (download).

**Rationale:** Implementers must understand they are wrapping existing operations, not inventing new ones. This prevents accidental duplication of encryption, token handling, or share-creation logic and keeps the interactive slice genuinely bounded.

**Implementation note:** The interactive layer should invoke the same underlying orchestration functions or CLI commands that non-interactive users would invoke manually. Do not re-implement validation or business logic inside the interactive module.

---

### 3. Non-TTY Behavior

**Decision:** When invoked in a non-TTY or unsupported terminal environment, interactive mode fails immediately with a clear error message; **no fallback to non-interactive mode**.

**Rationale:** Graceful degradation (e.g., "running in non-TTY, switching to non-interactive mode") creates confusion and unexpected behavior. Explicit failure signals the user that their environment is incompatible and suggests the correct approach: use non-interactive commands with explicit flags instead.

**Implementation note:** Use Spectre.Console's TTY detection or equivalent; emit exit code 1 with the message: `"Interactive mode requires a terminal. Use non-interactive commands with explicit flags for scripted or piped environments."`

---

### 4. Tightened Secret-Handling Rules

**Decision:** Interactive mode **inherits and strengthens** all secret-handling guarantees from prior plans:

- **Share keys:** Shown only on explicit opt-in (flag or user affirmation).
- **Token input:** Always masked (password-entry style).
- **Secrets in output:** Never rendered in normal diagnostics/warnings/errors.
- **Logging:** No secrets at any verbosity level.
- **No weakening:** Future convenience requests (e.g., "cache tokens for faster login") are rejected; prefer explicit opt-in.

**Rationale:** The upload, share-creation, and download plans established strict secret-lifecycle rules that cannot be weakened without violating zero-knowledge architecture. The interactive layer must uphold the same guarantees, even if it demands stricter UX choices (e.g., explicit opt-in for key display).

**Implementation note:** Review plans 0016, 0013, and 0017 secret-handling sections during implementation to ensure compliance.

---

### 5. Secret-Absence Test Assertions

**Decision:** Added an explicit acceptance criterion: **Orchestration and output tests must assert that secrets do not appear in rendered stderr or log output.**

**Rationale:** Accidental secret leaks in progress bars, error formatting, or diagnostics are a real risk with rich terminal libraries. Explicit test assertions using grep or regex patterns catch these leaks early.

**Implementation note:** Capture stderr and log output in tests, then assert the absence of plaintext share keys, bearer tokens, admin tokens, etc. using regex/grep. Example:
```csharp
var output = capturedStderr;
Assert.DoesNotMatch(@"[0-9a-f]{64}", output);  // No 256-bit hex key
Assert.DoesNotMatch(@"Bearer.*[A-Za-z0-9]", output);  // No bearer tokens
```

---

### 6. Scope Boundary (No Stateful Shell or New Logic)

**Decision:** Plan reinforced: keep interactive layer focused on **guided UX over existing operations**. Explicitly prohibits:

- Stateful shell or REPL.
- New business logic.
- Interactive-session state persistence.

**Rationale:** Scope creep here leads to maintenance burden and increased testing surface. If state or new logic becomes necessary, evaluate as a separate plan.

**Implementation note:** If implementers encounter feature requests like "remember server URL across sessions" or "add batch job scheduling," reject them as out of scope and suggest a separate follow-up plan.

---

## Files Modified

- `/home/chris/Code/github/ShadowDrop/ai-plans/0018-interactive-spectre-console-ux.md` — Updated Rationale, Acceptance Criteria, and Technical Details with all six clarifications.

---

## Team Guidance

- **Implementers:** Use this decision document to clarify expectations during implementation review.
- **Next steps:** When implementing, ensure the first acceptance criterion clearly documents your chosen invocation pattern (flag vs. subcommand).
- **Secret handling:** Use this document and prior plans 0016/0017 as a security checklist during code review.

---
# Plan 0018 Final Clarifications — Interactive Spectre.Console UX

**Date:** 2026-05-16T22:34:31.673+02:00  
**Lead:** Nate  
**Requested by:** chA0s-Chris

## Summary

Applied four clarifying edits to plan 0018 to lock down the interactive UX design while preserving security and scope boundaries.

## Decisions Made

### 1. Interactive Invocation Pattern

**Committed to:** `shadowdrop <subcommand> --interactive` pattern.

**Rationale:** Placing `--interactive` after the subcommand (e.g., `shadowdrop upload --interactive`) is more intuitive for users and aligns with how System.CommandLine typically structures verb-based parsers. This also avoids ambiguity about whether `--interactive` applies globally or to a specific operation.

**Impact:** All implementations of upload, download, and share-creation interactive modes will follow this pattern. Documentation and help text will be clear about placement.

---

### 2. Guided Share-Creation Workflow with Upload-Generated Key

**Clarified:** How the upload-generated share key flows through the interactive session while remaining secret.

**Workflow:**
1. User selects files in interactive upload prompt
2. Upload delegates to plan 0016 logic, which generates the share key
3. Share key is held in memory (opaque, never rendered yet)
4. Share configuration prompts (expiration, direct-HTTP, optional token) capture user choices
5. All captured choices + key are passed to plan 0013 share-creation logic
6. Share key is displayed to user **only on opt-in** (explicit prompt or `--output-secret` flag)
7. Secrets remain hidden by default; user controls visibility

**Impact:** Implementers now have a clear, step-by-step workflow that shows key ownership and opt-in flow. No ambiguity about when/if the key leaks to terminal.

---

### 3. Optional Download Bearer-Token Prompting

**Clarified:** How optional bearer-token protection integrates into share-creation and maintains secret handling.

**Design:**
- Token input uses **masked prompts** (Spectre.Console password mode) so characters are not echoed
- Token is hashed by plan 0013 logic (not by interactive layer)
- Plaintext token never appears in logs, diagnostics, or output
- Token is single-session-scoped; no caching or persistence beyond command invocation
- Interactive layer only orchestrates the prompt; plan 0013 owns hashing and validation

**Impact:** Clear separation of concerns: UI layer prompts & masks; business layer hashes & validates. Implementers know exactly where each responsibility lies.

---

### 4. Scope Boundaries Reinforced

**Confirmed:** Plan 0018 remains a UX wrapper over existing operations.

**Out of Scope:**
- No stateful REPL or long-lived interactive shell
- No new business logic (encryption, server features, batch operations)
- No session-to-session state persistence

**Impact:** Prevents scope creep. If future requests demand these features, they go to separate plans, not grafted into 0018.

---

## Team Implications

- **Implementers:** Workflow is locked and clear. Follow the `--interactive` pattern and keep all business logic delegated.
- **Tests:** Must verify secret-handling paths (no leaks in diagnostics) and orchestration correctness (delegates to 0016/0013/0017 operations).
- **Documentation:** Ensure user-facing help and README explain the `--interactive` flag placement and opt-in secret display behavior.

No GitHub issue update required at this time; plan clarifications are editorial and do not change acceptance criteria or scope.
# Scribe Decision: Issue #15 Sync

**Date:** 2026-05-16T21:28:18.558+02:00  
**Agent:** Scribe  
**Action:** Updated GitHub issue #15 body to mirror finalized plan

## What

Synchronized GitHub issue #15 ("Range requests and resumable downloads") with the finalized plan at `/home/chris/Code/github/ShadowDrop/ai-plans/0015-range-requests-and-resumable-downloads.md`.

Issue body now includes:
- Full rationale
- All 11 acceptance criteria with checkboxes
- Complete technical details covering:
  - Plaintext-Range-to-Chunk-Span Mapping
  - HTTP 206 Response Contract with Accept-Ranges polish note
  - Direct-HTTP vs CLI Resumable Semantics with locked CLI response shape
  - Optional download bearer-token validation reuse pattern
  - Non-leaky Range-Request Error Handling

## Why

Per user directive (2026-05-15T21:53:13.266+02:00): "When implementing an issue that has a corresponding plan in `./ai-plans/`, always update the plan and check all acceptance criteria met by the implementation."

This sync ensures:
- Issue serves as authoritative spec for team implementation
- Plan and issue remain synchronized source of truth
- All acceptance criteria visible and trackable on GitHub
- Security and technical details (polish notes, token validation pattern, error handling) captured for implementer reference
# PR #24 Security Review — Direct-HTTP Key Material Cleanup

**Reviewer:** Alec (Security Engineer)  
**Date:** 2026-05-17T23:05:01.413+02:00  
**Status Review:** 2026-05-18T00:28:54.318+02:00  
**Verdict:** ✅ **APPROVED FOR MERGE — SECURITY FIX VERIFIED**

## Issue

PR #24 implements direct-HTTP downloads with server-side decryption. The code correctly handles secret cleanup when `DirectHttpDecryptingStream.CreateAsync` fails after stream construction. However, a critical failure path remains unprotected: if blob storage fails to open *before* the stream takes ownership, the decoded HTTP key is never zeroed.

### Code Path

File: `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`, method `TryOpenDirectHttpContentAsync` (lines 121–148)

```csharp
private async Task<Stream?> TryOpenDirectHttpContentAsync(UploadedFileRecord uploadedFile,
                                                         String keyMaterial,
                                                         CancellationToken cancellationToken)
{
    Stream? encryptedContent = null;
    try
    {
        var secretBytes = Convert.FromBase64String(keyMaterial);  // ← Secret decoded to heap
        encryptedContent = await _blobStorage.OpenReadAsync(uploadedFile.BlobKey, cancellationToken);  // ← Can throw
        return await DirectHttpDecryptingStream.CreateAsync(encryptedContent,
                                                            uploadedFile,
                                                            secretBytes,
                                                            cancellationToken);
    }
    catch (Exception exception) when (exception is ArgumentException
                                               or CryptographicException
                                               or EndOfStreamException
                                               or FormatException
                                               or OverflowException)
    {
        if (encryptedContent is not null)
        {
            await encryptedContent.DisposeAsync();  // ← Clears stream, NOT secretBytes
        }

        return null;  // ← secretBytes remains in memory
    }
}
```

### Failure Scenario

1. `secretBytes` is decoded from base64 HTTP header/query parameter (line 128)
2. `OpenReadAsync` throws due to storage timeout, network error, or missing blob (line 129)
3. Catch block disposes `encryptedContent` but does not zero `secretBytes`
4. Exception is caught and converted to `DownloadLookupStatus.InvalidRequest`
5. **Result:** Request-scoped key material remains resident on heap indefinitely

### Attack Surface

- **Memory introspection:** Process dump, debugger pause, crash dump captures plaintext key
- **Garbage collection timing:** Secret may outlive the request if finalizer is delayed
- **Request handling pipeline:** Key material persists in ASP.NET Core pooled thread context

This is a **silent retention vulnerability**: the code explicitly clears secrets in the happy path (when stream is created and disposed), but leaks them on a plausible failure path.

## Required Fix

### Code Change

Add zeroing to the catch block:

```csharp
catch (Exception exception) when (exception is ArgumentException
                                           or CryptographicException
                                           or EndOfStreamException
                                           or FormatException
                                           or OverflowException)
{
    CryptographicOperations.ZeroMemory(secretBytes);  // ← ADD THIS
    if (encryptedContent is not null)
    {
        await encryptedContent.DisposeAsync();
    }

    return null;
}
```

### Test Addition

Add a test case that verifies `secretBytes` is zeroed when `OpenReadAsync` throws. This test does not currently exist; the existing test `ResolveAsync_ShouldDisposeEncryptedStreamWhenDirectHttpStreamCreationFails` uses a valid secret and only covers stream disposal, not key cleanup on the OpenReadAsync path.

Example stub:
```csharp
[Test]
public async Task TryOpenDirectHttpContentAsync_ShouldZeroKeyMaterialWhenOpenReadAsyncFails()
{
    // Create a stub BlobStorage that throws IOException on OpenReadAsync
    // Verify the passed secretBytes are zeroed after the exception is handled
}
```

## Related Review Comments

- **Comment r3255430555** (resolved): CreateAsync failure handling — fixed ✅
- **Comment r3255477328** (unresolved): OpenReadAsync failure handling — this issue

## Approval Gate

**Block merge until:**
1. ✅ Zero `secretBytes` in catch block (COMPLETED — WithDecodedDirectHttpKeyMaterialAsync wrapper)
2. ✅ Add test case for OpenReadAsync failure with secret verification (COMPLETED — line 295 test passes)
3. ✅ Code review confirms no other secret transfer points lack guards (VERIFIED — only one secret path)

---

## Security Verification (2026-05-18T00:28:54.318+02:00)

### Fix Implementation: VERIFIED ✅

**WithDecodedDirectHttpKeyMaterialAsync (lines 121-139):**
- ✅ Try-finally pattern: try block decodes and executes action, finally block zeros on exception
- ✅ Ownership gate: `if (!ownershipTransferred && secretBytes is not null)` prevents double-cleanup
- ✅ Exception handling: Finally executes regardless of exception type (IOException, ArgumentException, etc.)

**Test Coverage: VERIFIED ✅**

Test `WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroDecodedBytesWhenFailureOccursBeforeOwnershipTransfer` (lines 295-310):
- ✅ Captures decoded bytes in action scope
- ✅ Throws CryptographicException (simulates failure before ownership transfer)
- ✅ Verifies secret is zeroed: `capturedDecodedBytes!.Should().OnlyContain(value => value == 0)`
- ✅ Test passes successfully

**Failure Path Preservation: VERIFIED ✅**

- IOException and FileNotFoundException propagate correctly (not suppressed)
- Secret is zeroed BEFORE exception propagates (no heap retention)
- No masking of I/O errors or unrelated failures
- Crypto-buffer-encapsulation pattern correctly applied

### Conclusion

**SECURITY FIX IS CORRECT AND COMPLETE.** All acceptance criteria met. Safe for merge.

## Pattern Reference

This finding reinforces the `crypto-buffer-encapsulation` skill: all boundaries where secrets are created, transferred, or relinquished must have explicit cleanup guards on both success and failure paths.

See: `.squad/skills/crypto-buffer-encapsulation/SKILL.md`

### 2026-05-16T23:04:44.162+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Do not commit or push code changes unless explicitly requested. If asked to push a branch for PR setup, push only the empty branch so the PR can be created; do not add an empty commit for that.
**Why:** User request — captured for team memory

# Eliot decision inbox — final PR24 fixes

- **Date:** 2026-05-18T08:39:49.512+02:00
- **Area:** Upload reservation workflow and direct-download stream cleanup

## Decision

Make upload reservation consumption an explicit repository contract: `TryClaimReservationAsync` atomically moves a reservation into an in-flight claimed state before blob persistence, `TryCompleteReservationAsync` only succeeds for a claimed reservation, and `ReleaseClaimAsync` rolls the claim back on failed uploads.

## Why

The previous split between `HasActiveReservationAsync` and `TryCompleteReservationAsync` let two concurrent uploads race past validation and fail later in storage-specific ways. Moving the atomic boundary into the repository keeps the workflow backend-agnostic and preserves the API contract that stale or reused reservations are rejected as validation failures.

## Notes

`DirectHttpDecryptingStream` now shares its secret-zeroing logic across sync and async disposal paths so plain `using` is equivalent to `await using` for retained key material cleanup.

# Eliot — Issue #12 Upload Persistence Decisions

## Context
Issue #12 adds the first durable upload persistence slice in `ShadowDrop.Api`.

## Decisions

1. **Opaque local blob layout**
   - Local blob files are stored under `Storage:LocalRoot` using server-generated blob keys derived from the file id, with a two-character directory fan-out (`{first-two-hex}/{fileId}.blob`).
   - Original file names are persisted as metadata only and never influence filesystem layout.

2. **Owner-only filesystem permissions**
   - The local storage root and any derived blob directories are created with owner-only directory permissions where supported.
   - Blob files and the LiteDB metadata file are enforced to owner read/write only (`0600` equivalent) where supported.

3. **LiteDB shared-mode upload repository**
   - `LiteDbUploadedFileMetadataRepository` uses `ConnectionType.Shared`, matching the in-process access pattern already established for admin token storage.
   - This keeps WebApplicationFactory-based tests and future in-process readers from tripping exclusive file locks.

4. **Deterministic cross-layer cleanup**
   - Upload persistence writes the blob first, then metadata, and deletes the blob again if metadata persistence fails or if the written length does not match declared encrypted length.
   - The local blob storage implementation also deletes partially written blob files on write failures.

# Eliot issue 13 decisions

- Implemented share creation as `POST /api/admin/shares` with JSON fields `expiresAtUtc`, `files`, `directHttpEnabled`, `generateDownloadBearerToken`, and `downloadBearerTokenExpiresAtUtc`.
- In separate-key mode (`directHttpEnabled=false`), callers must explicitly set `generateDownloadBearerToken` to `true` or `false`; omission is rejected so the API preserves the plan's "configured or explicitly declined" invariant.
- Share metadata is stored atomically in a single LiteDB `shares` document with embedded file entries and optional embedded download-token metadata, while only SHA-256 hashes of share/download tokens are persisted.

---
date: 2026-05-18T11:19:54.273+02:00
author: Eliot
issue: 15
---

# Issue #15 range-download decisions

- Public download requests now honor standard single `Range: bytes=...` headers after the same share-token, bearer-token, revocation, and expiration checks as full downloads.
- Direct-HTTP shares return plaintext partial bodies with `206`, `Content-Range`, `Content-Length`, and `Accept-Ranges: bytes`, while decrypting only the chunk span that covers the requested plaintext bytes.
- CLI-decrypt shares keep the existing JSON resumable contract body for encrypted chunk subsets, but `Range` headers now drive the requested plaintext span and the endpoint responds with `206` plus a plaintext-oriented `Content-Range` header. Existing `plaintextStart` / `plaintextEndExclusive` query support remains for compatibility; CLI follow-up should migrate to `Range` headers and can later remove the query fallback.
- Unsatisfiable ranges now return generic `416` without file-size leakage, and malformed range inputs still collapse to the existing generic `400` download error payload.

# Eliot PR24 download content type fallback

- **Date:** 2026-05-18T09:39:48.140+02:00
- **Area:** Public download response handling

## Decision

Validate stored upload content types when composing the download response and fall back to `application/octet-stream` when the persisted value is missing or not a valid media type.

## Why

Upload metadata is persisted data, so older rows or externally modified records can contain malformed media types. Validating at download time keeps downloads resilient without tightening unrelated upload flows or making stored bad values crash response generation.

# Eliot — Upload Reservation Expiry Consistency

## Context
PR #24 surfaced a valid review note: expired upload reservations were only removed when a later reservation call triggered lazy pruning, so stale ids could still pass active-check or completion paths in the meantime.

## Decision
1. **Centralized active-reservation validity**
   - `LiteDbUploadedFileMetadataRepository` treats a reservation as active only when it is marked reserved, has a reservation timestamp, and that timestamp is newer than the retention cutoff.
   - The same validity rule is applied by both `HasActiveReservationAsync` and `TryCompleteReservationAsync`.

2. **Opportunistic expired-reservation pruning**
   - Existing lazy pruning on `ReserveFileIdAsync` remains.
   - In addition, when active-check or completion loads an expired reservation, the repository deletes it before returning `false`.

## Rationale
This keeps upload reservation behavior correct even if no subsequent reservation request happens before an upload attempt. It also avoids divergent interpretations of reservation state across repository methods, which is the root cause of the PR #24 finding.

# Issue #14 Remediation & Security Review — Assessment & Next Steps

**Lead:** Nate  
**Date:** 2026-05-17T23:50:41.301+02:00  
**Verdict:** ✅ Architecture Fixed | 🟡 Security Verification Pending

---

## Executive Summary

The direct-HTTP/upload cryptographic mismatch has been **architecturally resolved** via file-scoped binding (Option 1, commit cc41a1d). The secret key cleanup issue identified by Alec has been **proactively fixed** via the `WithDecodedDirectHttpKeyMaterialAsync` ownership-transfer helper (commit 1717f87). However, explicit test verification of the security fix is missing and must be added before merge.

**Critical Path Forward:**
1. ✅ Verify FileEncryptionContext changes preserve upload/download consistency
2. ✅ Confirm WithDecodedDirectHttpKeyMaterialAsync guards all secret paths
3. 🟡 **ACTION:** Add test case for OpenReadAsync failure with secret verification (Alec's block)
4. 🟡 **ACTION:** Document crypto-buffer-encapsulation pattern application

---

## Issue #14: Direct-HTTP/Upload Mismatch — RESOLVED

### The Problem

Uploads assign files with KDF salt determined at upload time. Shares are created independently afterward. Original direct-HTTP design bound decryption context to share-scoped values, creating a circular dependency: *normal uploads could not satisfy direct-HTTP mode if a share was created later.*

### The Solution Applied: File-Scoped Binding (Option 1)

**Changes:**
- `FileEncryptionContext` no longer takes `shareId` parameter (removed in cc41a1d)
- Crypto binding uses only `(fileId, kdfSalt)` — values determined at upload time
- Chunk authentication remains file-scoped, not share-scoped
- Any share of the uploaded file can decrypt using direct-HTTP mode

**Impact:**
- ✅ Removes the upload-time/share-time mismatch
- ✅ Preserves file-level isolation (all shares of same file use same file-scoped context)
- ✅ Keeps implementation minimal for MVP
- ✅ No schema changes or data migration required

**Verification:**
- Build succeeds with no warnings
- All 120 tests pass (68 Shared + 52 API)
- FileEncryptionContext contract correctly updated (see diff below)

---

## Alec's Security Finding — PARTIALLY FIXED

### The Vulnerability Identified

PR #24 comment identified a secret-retention path: if `_blobStorage.OpenReadAsync()` throws an exception before `DirectHttpDecryptingStream.CreateAsync()` takes ownership, the decoded HTTP key material (`secretBytes`) would not be zeroed in the original error handler.

**Timeline:**
- **2026-05-17T23:05:01:** Alec's security review flagged the issue
- **2026-05-17T23:14:52:** Commit 1717f87 proactively introduced `WithDecodedDirectHttpKeyMaterialAsync` helper
- **2026-05-17T23:28:51:** Commit ba74067 refactored `ContentKey` lifecycle for correctness

### The Fix Applied

Commit 1717f87 introduced a helper method with proper ownership-transfer semantics:

```csharp
internal static async Task<T> WithDecodedDirectHttpKeyMaterialAsync<T>(
    String keyMaterial, 
    Func<Byte[], Task<T>> action)
{
    Byte[]? secretBytes = null;
    var ownershipTransferred = false;
    try
    {
        secretBytes = Convert.FromBase64String(keyMaterial);  // ← Decode
        var result = await action(secretBytes);                // ← Action owns secret
        ownershipTransferred = true;
        return result;
    }
    finally
    {
        // ← Cleanup ALWAYS executes, regardless of exception type
        if (!ownershipTransferred && secretBytes is not null)
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }
}
```

**How it protects:**
1. Secret is decoded in the try block
2. If action throws **any exception** (IOException, CryptographicException, ArgumentException, or unknown types), the finally block executes
3. Finally block zeros the secret **unless ownership was transferred to the stream**
4. This guards against the original vulnerability: even if `OpenReadAsync` throws, the secret is zeroed before the exception propagates

**Current call site:**
```csharp
return await WithDecodedDirectHttpKeyMaterialAsync(keyMaterial,
    async secretBytes =>
    {
        encryptedContent = await _blobStorage.OpenReadAsync(...);  // ← Protected
        return await DirectHttpDecryptingStream.CreateAsync(
            encryptedContent,
            uploadedFile,
            secretBytes,
            cancellationToken);
    });
```

---

## Test Coverage Gap — MUST CLOSE BEFORE MERGE

### Current Test Coverage

Existing tests verify:
- ✅ Content key reuse across chunks (DirectHttpDecryptingStream_ShouldReuseDerivedContentKeyAcrossChunks)
- ✅ Stream disposal zeros key material (DirectHttpDecryptingStreamDisposeAsync_ShouldZeroRetainedContentKeyAndShareSecret)
- ✅ Failed stream creation zeros share secret (DirectHttpDecryptingStreamCreateAsync_ShouldZeroShareSecretWhenInitializationFails)

### Missing Test

**Test:** `WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroKeyMaterialWhenOpenReadAsyncThrows`

This test must verify:
1. Create a stub `IBlobStorage` that throws `IOException` (or any non-caught exception) on `OpenReadAsync` call
2. Call `TryOpenDirectHttpContentAsync` with valid key material and the failing storage
3. Verify the decoded `secretBytes` have been zeroed (inspection via reflection into the method scope or via wrapper instrumentation)
4. Confirm the method returns null gracefully (DownloadLookupStatus.InvalidRequest)

**Rationale:**
- Current tests do not exercise the `OpenReadAsync` failure path
- This test closes Alec's specific concern: "secretBytes remains in memory" on storage failure
- Ensures the refactored helper correctly guards all exception paths

---

## Architectural Review: Direct-HTTP Flow

### Upload → Share → Download Flow

1. **Upload (Issue #11):** File encrypted with file-scoped KDF salt, stored with metadata
2. **Share Creation (Issue #13):** Share references uploaded file by ID, inherits file-scoped crypto context
3. **Direct-HTTP Download (Issue #14):** Client provides share token + key material, server decrypts using file-scoped context

**Data Flow:**
```
Upload:   KdfSalt generated → stored with file metadata
Share:    Share created, references file by ID (no new salt)
Download: Client provides share token + key material
          Server uses file ID + stored KdfSalt to decrypt
          ✅ No share-scoped salt needed
          ✅ Normal uploads work with direct-HTTP shares
```

### Crypto Contract Verification

| Element                | Scoped To      | Determined By | Compatibility |
|------------------------|----------------|---------------|---------------|
| KDF Salt               | File           | Upload        | ✅ Available at download time |
| File ID                | File           | Upload        | ✅ Available at download time |
| Content Key (derived)  | File           | (fileId, salt) | ✅ File-level isolation preserved |
| Share Secret (input)   | Client-provided| Download request | ✅ Orthogonal to file context |
| Chunk authentication   | File           | Metadata      | ✅ No share scope required |

---

## Compliance with Project Concept

### Original Intent (PROJECT_CONCEPT.md)

> *"Direct-HTTP mode: the server receives the decryption key during download"*  
> *"Download links that can optionally include the decryption key for direct HTTP retrieval"*

**Alignment:**
- ✅ Direct-HTTP downloads supported without CLI decryption
- ✅ Server decrypts using client-provided key material
- ✅ Shares can be created with or without direct-HTTP mode enabled
- ✅ Normal upload-then-share workflow works with direct-HTTP mode

### MVP Acceptance Criteria (Issue #14)

All criteria from `ai-plans/0014-basic-download-endpoint.md`:

- ✅ Public download endpoint exists
- ✅ Endpoint resolves share by token and file ID
- ✅ Expiration validation at download time
- ✅ Optional bearer token validation
- ✅ Direct-HTTP mode accepts key material (header + query parameter)
- ✅ CLI decrypt mode streams encrypted content
- ✅ Appropriate headers (filename, content-length)
- ✅ Streaming responses (no buffering)
- ✅ Automated tests cover success and error paths

---

## Next Steps for Tara & Parker

### Pre-Merge Verification (Do NOT Merge Until Complete)

**Tara (Backend/Testing):**

1. **Add Missing Test Case**
   - Create `TryOpenDirectHttpContentAsync_ShouldZeroKeyMaterialWhenOpenReadAsyncThrows`
   - Verify secret cleanup when storage fails
   - Use a stub `IBlobStorage` that throws (see test stub pattern in history)
   - Run test and verify it passes

2. **Review Secret Paths End-to-End**
   - Scan `DownloadFileService.cs` for all locations where `keyMaterial` or `secretBytes` are handled
   - Verify each path either:
     - Passes secret to `WithDecodedDirectHttpKeyMaterialAsync` (protected), OR
     - Explicitly zeros memory after use
   - Document findings in PR review notes

3. **Spot-Check Crypto-Buffer-Encapsulation Compliance**
   - Review `.squad/skills/crypto-buffer-encapsulation/SKILL.md`
   - Verify direct-HTTP flow follows "create → use → zero" pattern
   - Check that `ContentKey` disposal happens in stream's DisposeAsync

**Parker (Code Review/Merge Gate):**

1. **Verify Test Coverage**
   - Confirm Tara's new test exercises the OpenReadAsync failure path
   - Run full test suite: `dotnet test` should pass with ≥120 tests
   - Spot-check test uses realistic failure scenario (IOException, not generic Exception)

2. **Crypto Review Checklist**
   - ✅ FileEncryptionContext no longer requires shareId (file-scoped)
   - ✅ ChunkMetadata no longer uses share context
   - ✅ WithDecodedDirectHttpKeyMaterialAsync guards all secret transfer points
   - ✅ ContentKey is properly disposed in stream.DisposeAsync
   - ✅ No secrets are logged or exposed in error messages

3. **Architecture Review**
   - Confirm direct-HTTP downloads align with "file-scoped context" decision
   - Verify that multi-file shares can all use direct-HTTP mode without rekeying
   - Spot-check one download path end-to-end: resolve → decrypt → stream

### Approval Gate

**Block merge until:**
1. ✅ `TryOpenDirectHttpContentAsync_ShouldZeroKeyMaterialWhenOpenReadAsyncThrows` test exists and passes
2. ✅ All secret paths are accounted for (Tara's scan)
3. ✅ Parker confirms test is realistic and crypto-contract is sound
4. ✅ Full test suite passes (120+ tests)

---

## Decision Record

- **Issue:** #14 (Basic Download Endpoint)
- **Sub-issue:** Direct-HTTP/Upload Mismatch + Security Verification
- **Decision:** 
  1. **Architecture:** File-scoped binding (Option 1) is correct and implemented
  2. **Security:** Alec's concern is fixed by WithDecodedDirectHttpKeyMaterialAsync, but test coverage gap must be closed
- **Action Items:**
  - Tara: Add OpenReadAsync failure test + secret path audit
  - Parker: Review test realism + crypto contract
- **Timeline:** Must complete before PR #24 merge
- **Escalation:** None at this stage; proceed to testing phase

---

## Appendix: FileEncryptionContext Contract Change

**Before (Share-Scoped):**
```csharp
public FileEncryptionContext(Guid shareId, Guid fileId, Byte[] kdfSalt)
public Guid ShareId { get; }
public Guid FileId { get; }
```

**After (File-Scoped):**
```csharp
public FileEncryptionContext(Guid fileId, Byte[] kdfSalt)
public Guid FileId { get; }
// ShareId removed — no longer part of crypto binding
```

**Impact:**
- Upload: Creates context with (fileId, kdfSalt) at upload time ✅
- Download: Creates context with same (fileId, kdfSalt) from stored metadata ✅
- Share: Reference file, context is inherited implicitly ✅

---

## Security Review Verification — ALEC (2026-05-18T00:28:54.318+02:00)

**Status:** ✅ **SECURITY FIX VERIFIED**

### Findings

1. **WithDecodedDirectHttpKeyMaterialAsync Implementation:** Correct
   - Finally block executes regardless of exception type (ArgumentException, CryptographicException, IOException, etc.)
   - Secret zeroing gate (`!ownershipTransferred && secretBytes is not null`) ensures cleanup only when stream did NOT take ownership
   - Pattern prevents silent secret retention on storage failures (missing blob, timeout, network error)

2. **Test Coverage:** Adequate
   - `WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroDecodedBytesWhenFailureOccursBeforeOwnershipTransfer` ✅ passes
   - Test verifies secret is zeroed when action throws exception (covers OpenReadAsync failure path)
   - No other secret handling paths exist outside WithDecodedDirectHttpKeyMaterialAsync wrapper

3. **Failure Path Guarantees:** Preserved
   - Narrow fix: only affects key material lifecycle, does not suppress I/O exceptions
   - I/O errors (IOException, FileNotFoundException) propagate correctly to caller
   - Secret is cleaned before exception propagates (no heap retention)
   - No masking of unrelated failures

4. **Crypto-Buffer-Encapsulation Compliance:** ✅
   - Secret create → decode → transfer → zero pattern correctly implemented
   - Transfer ownership gate prevents double-zeroing
   - All boundaries explicitly guarded

### Approval

This fix correctly addresses the PR #24 vulnerability identified in Alec's original security review. All acceptance criteria met. Safe to merge.

---

## Sign-Off

**Alec (Security Engineer) Review Status:** ✅ **SECURITY APPROVED FOR MERGE**

The missing-blob handling fix preserves all secret-handling and failure-path guarantees. The fix is narrow and does not mask unrelated I/O failures.

**Nate (Lead) Review Status:** ✅ **READY FOR MERGE**

Architecture is sound. Security fix is verified correct. Test coverage is adequate. Ready for production.

**Next checkpoint:** Parker's final code review and merge approval.

---
date: 2026-05-18T11:19:54.273+02:00
issue: #15
title: Range Requests & Resumable Downloads — Slice Boundaries & Architecture
---

# Issue #15: Slice Decomposition & Team Contracts

## Summary

Issue #15 adds HTTP range-request support (206 Partial Content) and deterministic CLI-resumable download contracts on top of the existing chunked encryption model. This decision splits the work into three focused slices with clear architectural boundaries.

## Branch Name

**Recommended:** `squad/15-range-requests-and-resumable-downloads`

Follows established naming convention: `squad/{issue}-{slug}` from `main`.

## Slice 1: Direct-HTTP Range Infrastructure (Eliot, Primary)

**Scope:** Backend HTTP range-request handling for direct-HTTP download mode.

**Responsibility:** Eliot (backend), Parker (testing).

### Key Tasks

1. **Range Request Parsing Service** — New type: `HttpRangeRequestParser` or similar
   - Parse `Range: bytes=start-end` header
   - Validate format and reject multipart ranges (HTTP 400)
   - Return parsed range or error

2. **Plaintext Range → Chunk Span Mapping** — New service: `RangeResolutionService` or extend `DownloadFileService`
   - Given plaintext byte range `[start, end)` and chunk size, compute:
     - First chunk index and last chunk index
     - Offset within first chunk
     - End offset within last chunk
   - Reuse `ChunkRange` class; validate consistency
   - Reject unsatisfiable ranges (e.g., start ≥ file size) with HTTP 416

3. **Streaming Chunk Extraction** — Extend `IBlobStorage` or create `IBlobStorage.ReadChunksAsync(firstChunk, lastChunk)`
   - Open blob stream
   - Seek and read only required encrypted chunks
   - Do not materialize entire file or all chunks upfront
   - Stream-oriented to match existing storage design

4. **Selective Decryption** — Extend `ChunkEncryptionService` or create `RangeDecryptionService`
   - Decrypt only required chunk span
   - Trim plaintext result to exact byte range requested
   - Return only `[start, end)` bytes, not entire decrypted chunk span

5. **HTTP Response Lifecycle** — Modify `DownloadEndpoints` and `DownloadStreamResult`
   - Detect `Range` header in request
   - If present: resolve range, decrypt selective chunks, return 206 + `Content-Range` + `Content-Length` headers
   - If absent: existing full-file behavior (200 OK)
   - **Always include `Accept-Ranges: bytes` header** on all responses (200, 206, 4xx, 5xx) to advertise range capability

6. **Non-Leaky Error Handling**
   - Invalid range (416): no Content-Range, no file size hints
   - Invalid token (401): generic message, no token validation details
   - Expired share (401): same generic message, no expiration hints
   - Invalid bearer token (403): no differentiation between absent/invalid
   - Invalid range format (400): no reflection of malformed header

7. **Authentication & Expiration Gates** — Reuse existing logic
   - All range requests enforce same share-token validation as full-file downloads
   - All range requests enforce same optional bearer-token validation
   - All range requests enforce same expiration checks
   - No new authentication paths; apply existing gates before range resolution

### Key Files to Touch

- `DownloadEndpoints.cs` — Add Range header parsing, route to range handler, add Accept-Ranges to responses
- `DownloadFileService.cs` — Extend to handle range resolution
- `DownloadFileResolution.cs` — May extend to include range metadata (start, end, total length) OR create `RangeDownloadResolution` type
- **New:** `RangeResolutionService.cs` — Plaintext range → chunk span + selective decryption
- **New:** `HttpRangeRequestParser.cs` — RFC 7233 range header parsing
- **New:** Range-related types in `Contracts` or `Crypto` namespace if shared with CLI

### Tests (Parker)

- ✓ Full-file request (no Range header) → 200 OK + all content
- ✓ Aligned range (chunk-boundary aligned) → 206 Partial + correct Content-Range
- ✓ Mid-chunk range → 206 Partial + correct bytes + correct Content-Range
- ✓ Multi-chunk range → 206 Partial + all required chunks decrypted, trimmed to range
- ✓ Unsatisfiable range (start ≥ file size) → 416 Range Not Satisfiable (no Content-Range, no size leak)
- ✓ Invalid range format → 400 Bad Request (no reflection of header)
- ✓ Multipart ranges → 400 Bad Request (unsupported)
- ✓ Expired share + range request → 401 Unauthorized (no expiration hint, no file size)
- ✓ Invalid bearer token + range request → 403 Forbidden (no token validation details)
- ✓ Invalid share token + range request → 401 Unauthorized (no differentiation from expiration)
- ✓ Range header present on full-file response (200) + Add `Accept-Ranges: bytes` on all responses (200, 206, 4xx, 5xx)

---

## Slice 2: CLI Resumable Download Contract (Sophie, Primary)

**Scope:** CLI-specific endpoint or query-parameter mode for encrypted-subset downloads.

**Responsibility:** Sophie (CLI), Eliot (backend support), Parker (testing).

### Key Tasks

1. **CLI Query Routing** — Modify `DownloadEndpoints`
   - Detect CLI-specific query parameter, e.g., `?mode=cli-resumable` or similar routing logic
   - Route to separate handler: `CliResumableDownloadAsync` or similar
   - Apply same authentication & expiration gates

2. **CLI Resumable Response Contract** (locked)
   - JSON response containing:
     - `firstChunkIndex` (int64)
     - `lastChunkIndex` (int64)
     - `encryptedPayload` (base64 string of concatenated encrypted chunks)
     - `requestedRange` object with `start` (int64) and `end` (int64)
     - `totalPlaintextSize` (int64)
     - `chunkSize` (int32)
     - `finalChunkPlaintextLength` (int32)
   - Zero plaintext bytes in response; CLI decrypts locally
   - Metadata is deterministic and versioned for forward compatibility

3. **Encrypted Payload Generation** — New service or extend storage
   - For a requested plaintext range, extract encrypted chunks from first to last
   - Concatenate chunk ciphertexts
   - Encode as base64 for JSON transport
   - Do not materialize entire blob; stream chunks from storage

4. **Range Handling** — Coordinate with Slice 1
   - Support optional `?range=start,end` query parameters
   - If absent: return full-file encrypted chunks
   - If present: validate range, map to chunk span, return only required encrypted chunks

5. **Error Handling** — CLI-specific
   - Same non-leaky error semantics as Slice 1
   - Invalid range parameters → 400 Bad Request (generic)
   - Invalid token → 401 Unauthorized (generic)
   - Expired share → 401 Unauthorized (generic)

### Key Files to Touch

- `DownloadEndpoints.cs` — Add CLI mode routing
- **New:** `CliResumableDownloadHandler.cs` or inline handler in Endpoints
- **New:** `CliResumableDownloadContract.cs` or similar type (shared with CLI)
- May reuse: `RangeResolutionService` from Slice 1 for chunk-span mapping
- May reuse: `IBlobStorage` streaming logic from Slice 1

### Tests (Parker)

- ✓ Full-file CLI request → JSON response with all chunks, correct totalPlaintextSize
- ✓ Aligned-range CLI request → JSON response with correct firstChunkIndex/lastChunkIndex
- ✓ Mid-chunk range CLI request → JSON response with correct encryptedPayload span
- ✓ Multi-chunk range CLI request → JSON response with all required chunks
- ✓ CLI request + unsatisfiable range → 400 Bad Request (no range metadata)
- ✓ CLI request + invalid token → 401 Unauthorized (no token details, no file size)
- ✓ CLI request + expired share → 401 Unauthorized (no expiration hint)
- ✓ Resumable contract shape determinism — same input always produces identical response

---

## Slice 3: Cross-Slice Testing & Security Verification (Parker, Eliot, Sophie)

**Scope:** Comprehensive test coverage for both modes and security boundaries.

**Responsibility:** Parker (primary), with Eliot and Sophie for domain knowledge.

### Key Tests

1. **Direct-HTTP Mode Coverage**
   - See Slice 1 tests above (11 items)

2. **CLI Mode Coverage**
   - See Slice 2 tests above (8 items)

3. **Security & Leakage Tests**
   - Invalid range must not reveal file size
   - Invalid token must not differentiate between missing/invalid/revoked
   - Expired share must not expose expiration timestamp
   - Error responses use generic HTTP codes (400, 401, 403, 416) without detailed messages
   - No Content-Range header on error responses
   - No file metadata (chunk size, count, final-chunk length) in error bodies

4. **Resumability Tests**
   - Download first chunk with range, interrupt, resume from where stopped
   - Verify encrypted payload matches full-file equivalent
   - Verify CLI-side decryption produces identical plaintext as full-file download

5. **Authentication Integration Tests**
   - Share token validation applies to range requests
   - Optional bearer token validation applies to range requests
   - Expiration checks apply to range requests at request-time (no soft-expiration cache misses)

---

## Architecture & Constraints

### Existing Contracts (Reuse)

- `ChunkRange` — Already validates single-chunk offset consistency; use for range mapping
- `ChunkEncryptionService` — Decrypt by chunk; extend/wrap for selective decryption
- `IBlobStorage` — Extend with `ReadChunksAsync(firstChunk, lastChunk)` or stream-oriented method
- `IShareMetadataRepository` — Existing share lookup; no changes needed
- `DownloadFileService` — Existing auth/expiration logic; extend to dispatch to range handler

### New Contracts (Define)

- `HttpRangeRequest` — Parsed Range header (start, end) with validation
- `ChunkSpan` — First/last chunk indices + offsets (extension of ChunkRange concepts)
- `CliResumableDownloadResponse` — JSON contract for CLI (locked for forward compatibility)
- `RangeResolutionService` — Plaintext range + file metadata → chunk span + decryption window

### Streaming & Memory Constraints

- **No full-file materialization** — Always stream encrypted chunks from blob storage
- **Selective decryption** — Only decrypt required chunks; trim plaintext to exact byte range
- **Bounded buffers** — Chunk buffers (e.g., 64KB) reused; no per-file allocation
- **Accept-Ranges header** — Signals to clients that resumption is safe

### Security Constraints

- **Non-leaky errors** — Generic HTTP codes, no range/file/token hints in responses
- **Fixed-time token comparison** — Reuse existing pattern from share-creation
- **Expiration checked at request-time** — No background expiration; validated synchronously
- **Consistent auth gates** — Range requests must pass same checks as full-file downloads

---

## Acceptance Criteria — Issue #15

All items from issue acceptance criteria are addressed by the three slices:

- ✓ Support single HTTP byte ranges (Slice 1)
- ✓ 206 Partial Content + correct headers (Slice 1)
- ✓ Reject invalid ranges without leaking file size (Slice 1)
- ✓ Enforce same auth/expiration as full downloads (Slice 1 integration)
- ✓ Direct-HTTP mode decrypts only needed chunks (Slice 1)
- ✓ CLI resumable contract locked (Slice 2)
- ✓ No full-file memory load (all slices, streaming design)
- ✓ Respect chunk boundaries and final-chunk logic (Slice 1 range mapping)
- ✓ Automated coverage for all cases (Slice 3 tests)
- ✓ Non-leaky error handling (Slice 1 + Slice 2)

---

## Implementation Order

1. **Slice 1 (Eliot + Parker):** Range infrastructure, HTTP 206 handling, tests
   - Backend foundation; unblocks both direct-HTTP mode and CLI mode
2. **Slice 2 (Sophie + Eliot + Parker):** CLI contract, encrypted-subset response
   - Depends on Slice 1 range mapping; can work in parallel after Slice 1 foundation is solid
3. **Slice 3 (Parker):** Cross-mode security and resumability verification
   - Final validation after Slices 1 & 2 are complete

---

## Decision Binding & Next Steps

- Branch name: **`squad/15-range-requests-and-resumable-downloads`**
- Assigned to: **Eliot (lead), Sophie (CLI), Parker (testing)**
- Review gate: **Nate + Parker** (ranged request logic, security); **Alec escalated** for token/auth details
- Status: **Ready for implementation**

All acceptance criteria binding. Slices are designed to avoid entanglement and support parallel testing. Non-leaky error handling is mandatory; no file size, chunk details, or token validation logic may leak in responses.

# Parker decision inbox — final PR24 tests

- Date: 2026-05-18T08:39:49.512+02:00
- Agent: Parker

## Decision
For PR #24 regression coverage, direct-HTTP decrypting stream disposal must be parity-tested for both `DisposeAsync()` and synchronous `Dispose()`, because secret-zeroing is a security property rather than an async-only implementation detail.

## Why it matters
A caller using synchronous disposal on the returned stream must still clear retained key material and close the wrapped encrypted stream. Coverage now treats the reservation-claim loser as a validation-path regression as well, so concurrent duplicate upload attempts cannot silently regress into raw storage failures.

## Evidence
- `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`
- `tests/ShadowDrop.Api.Tests/Uploads/UploadPersistenceServiceTests.cs`
- `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`

# Parker — Issue #15 test strategy

Date: 2026-05-18T11:19:54.273+02:00

## Decision

Use a two-layer test split for issue #15:

1. Keep chunk-span math and slice correctness in shared crypto tests, where `ChunkEncryptionService.GetChunkRange` and range round-trip coverage already exercise aligned, mid-chunk, and multi-chunk plaintext windows deterministically.
2. Add API regressions only for security behavior that the current endpoint already supports: range-shaped requests with invalid share tokens, expired shares, missing bearer tokens, or invalid direct-HTTP key material must fail generically and must not emit partial-content headers.

## Gap requiring follow-up

The current public download endpoint in `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs` does not parse the HTTP `Range` header and never returns `206 Partial Content`, `416 Range Not Satisfiable`, or `Content-Range` / `Accept-Ranges` headers. Because that production hook does not exist yet, API regression coverage for full-file-vs-range behavior, aligned ranges, mid-chunk ranges, multi-chunk ranges, and unsatisfiable ranges is still blocked at the HTTP layer.

## Routing note

Once the endpoint accepts single-range requests, add API walking tests that prove:
- full-file responses advertise resumability correctly
- aligned, mid-chunk, and multi-chunk ranges return exact plaintext slices with `206`
- unsatisfiable ranges return `416` without leaking file size details beyond the final agreed contract
- authorized range requests and unauthorized/expired/forbidden range requests remain behaviorally distinct without returning partial content

# Reservation expiry regression coverage

Date: 2026-05-18T08:27:12.481+02:00
Agent: Parker

## Decision
Treat expired upload reservations as invalid immediately, even before a later reservation request prunes them, and keep regression coverage at both backend and API upload boundaries.

## Evidence
- `UploadPersistenceServiceTests` now verifies the LiteDB repository reports expired reservations inactive, refuses completion, and the persistence service rejects expired file ids without writing blobs.
- `ApiWalkingSkeletonTests` now verifies `/api/admin/uploads` returns 400 and persists nothing when a previously issued reservation has expired in storage.

## Why it matters
This protects the fix for PR #24 against regressions where stale reservation rows remain in LiteDB until another reservation request triggers cleanup.

# Parker: reserved upload id coverage

- Direct-HTTP decryption only works through the real upload path when the encrypted payload's file-scoped crypto context matches the uploaded file id.
- Regression coverage now needs a real reservation -> upload -> share -> direct-download path, plus a negative case for uploads that skip reservation.

# Sophie — Issue 15 CLI Contract Decision

- **Date:** 2026-05-18T11:19:54.273+02:00
- **Area:** CLI download contract / resumable encrypted-subset mode

## Decision

Lock the CLI resumable-download response as a JSON contract carried by the existing public download endpoint in CLI-decrypt mode, with these required fields:

- `firstChunkIndex`
- `lastChunkIndex`
- `encryptedPayload` (Base64)
- `requestedRange.start`
- `requestedRange.end` (exclusive)
- `totalPlaintextSize`
- `chunkSize`
- `finalChunkPlaintextLength`

The endpoint now also returns stable headers for operator ergonomics:

- `X-ShadowDrop-Download-Mode`
- `X-ShadowDrop-File-Name`
- `X-ShadowDrop-File-Content-Type`

## Why

This keeps the CLI path deterministic and scriptable without inventing a second endpoint. The JSON body is explicit enough for resume bookkeeping and local decryption planning, while the headers preserve output-file intent without overloading the contract itself.

## Current boundary

Direct-HTTP HTTP-range support is still not implemented in this branch. The CLI-decrypt path can now request encrypted chunk subsets via `plaintextStart` and `plaintextEndExclusive`, but the remaining backend work for issue #15 is the direct-HTTP `Range`/`206 Partial Content` path and its non-buffering response behavior.

---
date: 2026-05-16T22:42:54.257+02:00
actor: Sophie
---

# Issue #18 Body Synced with Plan File

## Context

Issue #18 ("Interactive Spectre.Console UX") had an empty body. The corresponding detailed plan file (`ai-plans/0018-interactive-spectre-console-ux.md`) contains comprehensive acceptance criteria, technical details, and testing requirements for the interactive terminal UX feature.

## Decision

Synced the issue #18 body with the full content of the plan file to maintain a single source of truth for the feature specification. The plan file remains unchanged; only the GitHub issue body was updated.

## Outcome

- Issue #18 body now contains the complete rationale, acceptance criteria, technical details, and scope boundaries from the plan file
- Users and collaborators can reference the issue directly for the full feature specification
- Plan file remains the authoritative source for implementation details

# Upload reservation contract for file-scoped encryption

## Decision

For upload flows that bind cryptography to `fileId`, the server must reserve the file identifier before the client encrypts any bytes.

## Minimal contract

- `POST /api/admin/uploads/reservations` returns a server-generated `fileId`.
- The client includes that exact `fileId` in upload metadata.
- The upload API only completes persistence for previously reserved ids.

## Why

This keeps the existing upload workflow intact while making the file-scoped crypto context consistent with direct-HTTP download decryption. It also avoids inventing a second upload product path and keeps queue/key handling unchanged.

# Tara — Reserved upload file id for file-scoped crypto

- Direct-HTTP remains file-scoped, but upload encryption now depends on a server-issued `fileId` reserved before the client encrypts.
- The API persists a reservation first, then only completes upload metadata for that reserved id; unreserved or already-consumed ids are rejected.
- This keeps local and CI behavior aligned with a single repeatable upload path and avoids reintroducing share-derived crypto context.

---
date: 2026-05-18T13:15:18.889+02:00
---

# Eliot — CLI Range Fix: Streaming Encrypted Payload (v1 Contract Lock)

**Date:** 2026-05-18T13:15:18.889+02:00  
**Author:** Eliot  
**Scope:** API download / CLI resumable contract

## Decision

Keep the current CLI resumable-download JSON contract for v1, but stream the encrypted payload field instead of materializing the full encrypted chunk span into a single `byte[]` before Base64 encoding.

## Why

- Removes the large-range array-size and memory pressure failure mode without breaking the current CLI wire contract.
- The API should keep using `ContractsJsonSerializerContext` as the shared source of truth for the contract shape, even when the payload bytes are emitted incrementally.
- A streamed binary v2 contract is still worth doing later for payload efficiency; follow-up issue created: #25.

## Notes

- Treat the current JSON shape as compatibility-sensitive for existing CLI consumers.
- Preserve regression coverage that proves large CLI ranges are not eagerly buffered.

# Nate — Issue #25 Created: CLI Resumable Downloads v2 Contract Migration

**Date:** 2026-05-18T13:15:18.889+02:00  
**Author:** Nate (Lead)  
**Area:** CLI Downloads & API Contracts

## Decision

Create a GitHub issue to track migration from CLI resumable download contract v1 (JSON + Base64) to v2 (streamed binary + deterministic metadata).

## Rationale

**Current (v1) constraints:**
- Base64 encoding overhead: 33% payload size increase over raw binary
- Buffering inefficiency: entire encrypted chunk span must be materialized and encoded before transmission
- Not natural for binary file downloads (JSON wrapper adds complexity on CLI side)

**v2 motivation:**
- Streaming binary reduces payload and buffering overhead
- More idiomatic for secure file transfer
- Same security posture as v1 (no trust boundary changes)
- Backward compatible: v1 remains default, v2 is opt-in via query parameter

**Timing:**
- NOT blocking issue #15 (v1 completes this sprint with buffers fixed)
- Create as future placeholder so team doesn't forget
- Implement after #15 is merged and v1 CLI consumption is validated in production

## Scope: Issue #25

**Title:** "CLI Resumable Downloads: Migrate to Streamed Binary v2 Contract"

**Contract v2 Shape (sketch):**
- Streaming response with binary encrypted chunk span (no JSON wrapper)
- Metadata delivered as deterministic preamble or footer:
  - First/last chunk indices
  - Plaintext range boundaries
  - Total plaintext file size
  - Chunk size and final-chunk plaintext length
- Query routing: `?format=binary-v2` or `?contract=v2` to select v2 path
- Security: same auth/expiration/range-validation gates as v1
- CLI behavior: autodetect v2 availability, fall back to v1 if unsupported

**Scope Items:**
- Design v2 binary contract (preamble vs. footer, serialization format)
- Extend endpoint to support dual-mode routing
- Implement v2 response construction
- Add CLI autodetection and fallback
- Benchmark payload and performance vs. v1
- Security review of metadata in streaming context
- Parallel test coverage for v1 and v2

**Non-Goals:**
- No breaking changes to v1
- Not for immediate implementation (future work)

## Related

- **Issue #15:** Range Requests and Resumable Downloads (v1 implementation, locked contract)
- **Plan:** `ai-plans/0015-range-requests-and-resumable-downloads.md` (v1 contract locked; v2 direction added as context)
- **GitHub Issue #25:** https://github.com/chA0s-Chris/ShadowDrop/issues/25

# Parker — CLI Range Fix Regressions: Dual-Edge Coverage

**Date:** 2026-05-18T13:15:18.889+02:00  
**Author:** Parker (Tester)

## Decision

Keep regression coverage for the resumable CLI range path split across API and CLI layers:
- API tests should compare emitted JSON bytes against `ContractsJsonSerializerContext` serialization so the producer cannot silently drift back to reflection-based serialization.
- CLI parser tests should include chunk-index and plaintext-range values far beyond `Int32` limits so Eliot's current JSON/Base64 contract stays protected against the prior large-span regression.

## Why

The serializer-context review finding is producer-side, while the large-span regression risk is consumer-side. Covering both edges directly keeps the current v1 contract honest until the planned streamed-binary v2 follow-up replaces it.

