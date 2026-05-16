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
