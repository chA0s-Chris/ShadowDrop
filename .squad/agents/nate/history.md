# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Lead for ShadowDrop.
- Project emphasis: secure temporary file handoff, vertical slices, and narrow MVP scope.
- 2026-05-14T20:30:32.289+02:00 — Issue #2 design review completed. Settled API, AAD format, nonce scheme, key derivation, and test surfaces. Decision written to `.squad/decisions/inbox/nate-issue-2-crypto-design.md`. Branch `squad/2-chunked-aes-gcm-crypto-spike` created from `main` and pushed.
- Key architectural decisions for issue #2: deterministic nonce from chunk index (no stored nonce), 50-byte fixed-size AAD, HKDF-SHA-256 with file-specific info blob, `ChunkEncryptionService` as single sealed class (no interface in spike), `stackalloc` for AAD to avoid heap allocation, `IDisposable` + `ZeroMemory` on `ShareSecret` and `ContentKey`.
- Test projects use NUnit 4 + FluentAssertions. Prefer sociable unit tests. No Moq/NSubstitute — manual test doubles only.
- No `dev` branch in this repo; issue branches are `squad/{issue}-{slug}` from `main`.
- Key file paths for crypto spike: `src/ShadowDrop.Shared/Crypto/` (production), `tests/ShadowDrop.Shared.Tests/Crypto/` (tests).
- 2026-05-14T23:46:26.915+02:00 — PR #6 code review completed. Three substantive issues found:
  1. **KdfSalt mutability:** `FileEncryptionContext.KdfSalt` property returns mutable array reference → encapsulation breach. Fix: defensive copy in getter.
  2. **KDF salt per-file design violation:** `Generate()` factory creates new salt per invocation, violates per-share design. Fix: remove or restrict to tests; enforce explicit salt reuse.
  3. **Temporary key material cleanup:** `DeriveContentKey()` leaves intermediate key buffer uncleared. Fix: explicit zeroing in finally or use crypto pool.
  All three are actionable and architectural/security-relevant, not style. PR needs changes before merge.

- 2026-05-15T00:12:35.582+02:00 — Assessed three unresolved Copilot review notes on PR #6. Note 3 (test doesn't exercise the behavior it claims) is a must-fix — the secret is never disposed before the crypto ops, so the test gives false confidence. Note 1 (ChunkRange single-chunk offset validation gap) and Note 2 (EncryptedChunk.Ciphertext mutable getter) are both should-fix — real correctness/encapsulation issues, not style. All three recommended for fixing before merge.
- 2026-05-15T00:16:55.489+02:00 — Re-reviewed PR #6 after Tara's revision request. The earlier KDF-salt and temporary-key-material findings are fixed in `FileEncryptionContext` and `ChunkEncryptionService`, but the three open notes remain unresolved in `ChunkRange`, `EncryptedChunk`, and `ChunkEncryptionServiceTests`. Reviewer outcome: PR still not ready to merge until those three blockers are actually addressed and covered by tests.
- 2026-05-15T00:16:55.489+02:00 — Re-reviewed Tara’s follow-up on PR #6. The three open blockers are now actually fixed: `ChunkRange` rejects inconsistent single-chunk offsets, `EncryptedChunk` exposes defensive copies while keeping an internal zero-copy `ReadOnlySpan` path for crypto operations, and the disposed-share-secret test now disposes the secret before encrypt/decrypt. Targeted verification: `dotnet test tests/ShadowDrop.Shared.Tests/ShadowDrop.Shared.Tests.csproj --filter "FullyQualifiedName~ShadowDrop.Tests.Crypto"` passed with 51 tests.
- 2026-05-15T16:11:44.855+02:00 — Implemented pre-user review gate policy: formalized in `.squad/routing.md` under "Review Gate (Pre-User Review)" section. Default pair is Nate + Parker. Alec escalates automatically for security-sensitive work (auth, tokens, crypto, secrets, permissions). Follows strict lockout semantics from reviewer-protocol SKILL. Decision documented in `.squad/decisions/inbox/nate-pre-user-review-gate.md`.

## 2026-05-15: Review Gate Formalization & Inbox Merge

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Nate's pre-user review gate policy and test coverage gap work merged into canonical `decisions.md`. All 9 inbox entries (Nate, Alec, Eliot, Tara, Parker) now formalized in team memory. Review gate enforcement routed to Coordinator for all future work.

- **Status:** Merged; ready for enforcement
- **Next:** Coordinator applies gate on all future work

## 2026-05-15: PR #10 Review-Note Follow-Up Assessment & Implementation Verification

**Session:** Scribe (2026-05-15T19:44:15.000Z)

Nate + Parker joint assessment of remaining Copilot review notes on PR #10. Identified three critical fixes:
1. Missing queue-file length validation (bounds checking in parser validator, not constructor)
2. Insufficient target URL validation (must be absolute HTTP(S) only)
3. Incomplete XML documentation on shared contracts

Recommendation passed to Eliot for implementation. Eliot completed all three fixes on current branch:
- `QueueFileParser` validates length bounds
- `QueueFileContract.target` rejects relative URLs
- XML docs added to `FileMetadataContract`, `QueueFileContract`, shared surface

Parker re-reviewed post-implementation and signed off. Orchestration logs written for all three agents. Decision "Shared Queue Contract Shape" documented in `decisions.md`.

- **Status:** Complete; PR #10 ready for merge
- **Decision:** "Shared Queue Contract Shape" formalized in `decisions.md`

## 2026-05-15T22:41:03.231+02:00: Upload Plan Refinement — Four Accepted Suggestions Applied

**Session:** Nate

Applied four substantive refinements to `ai-plans/0011-upload-api-and-encrypted-file-intake.md`:

1. **Upload response contract tightened:** Response now explicitly returns **only** file id and downstream-safe metadata (plaintext length, encrypted length, chunk count, encryption format version, algorithm id, chunk size) with no secrets, derived keys, or internal state. Prevents accidental `KdfSalt` or `ShadowDrop-Key` exposure.

2. **Error response safety requirement:** Error responses must not expose secrets, key material, system paths, or internal validation details. Errors use generic HTTP codes (400, 401, 413, 429) with minimal public message surface. Removes attacker inference surface.

3. **Abuse protection gate:** Upload endpoint enforces rate limiting or equivalent abuse protection to prevent high-volume upload spam. Adds security boundary beyond operational polish.

4. **All-or-nothing upload semantics:** Failed uploads must roll back partially committed metadata and file content. No orphaned records remain in store. Keeps audit trail clean and prevents blocking on retry.

Decision written to `.squad/decisions/inbox/nate-upload-plan-refinement.md`. Scope boundary reinforced: plan does not expand into share creation or download. Acceptance criteria are now binding for implementation team.

## 2026-05-15T20:41:03Z: Inbox Merge — Upload Plan Refinement

**Session:** Scribe (merge and team context sync)

Nate's upload plan refinement decision merged from inbox into canonical `decisions.md` under "Upload API & Intake" section. Inbox file deleted. Orchestration and session logs written. Cross-team notification delivered (history.md updated).

## 2026-05-15T22:48:25+02:00: Upload Plan Clarifications — Request from Christian Flessa

**Session:** Nate (current)

Applied two agreed clarifications to `ai-plans/0011-upload-api-and-encrypted-file-intake.md` in the Technical Details section:

1. **Metadata validation upfront:** Reject malformed envelope metadata (invalid lengths, inconsistent format, missing required fields) **before starting to consume the request body stream**. Prevents wasting bandwidth on invalid uploads and enforces strict parsing contract.

2. **Cross-layer cleanup semantics:** Clarified that all-or-nothing rollback must span **every persistence layer** in the upload path: if blob content is written before metadata commit succeeds, that content must be deleted. No orphaned state may remain in any storage layer (database, filesystem, or other persistence backend).

**Rationale:** Both clarifications tighten the contract for implementation without expanding slice scope. Metadata validation gates the streaming pipeline; cross-layer cleanup prevents silent failures and audit inconsistencies. Decision documented for team reference.

Slice boundary remains tight: upload is intake-only. Share creation, download setup, and token refresh belong in later slices.

## 2026-05-15T20:49:08Z: Upload Plan — Streaming-Gate & Cross-Store Rollback Clarifications

**Session:** Scribe (orchestration log capture)

Manifest note: Nate updated `ai-plans/0011-upload-api-and-encrypted-file-intake.md` with final streaming-gate and cross-store rollback clarifications. Ensures plan integration points are explicit and implementation team has clear boundaries on failure handling semantics.

## 2026-05-15T20:54:05Z: Upload Plan Clarifications — Inbox Merge & Decision Formalization

**Session:** Scribe (team coordination)

Nate's upload plan clarifications decision successfully merged from `.squad/decisions/inbox/` into canonical `decisions.md`. Two critical technical clarifications now formally documented for team reference:

1. **Metadata validation gate:** Metadata envelope must be fully validated and parsed before consuming request body stream. Prevents wasting bandwidth on malformed uploads and establishes clear synchronous parsing contract.

2. **Cross-layer cleanup semantics:** All-or-nothing rollback must span database, filesystem, and all other persistence layers. If blob write succeeds but metadata commit fails (or vice versa), both must be rolled back atomically. No orphaned state permitted.

Decision now appears in canonical `decisions.md` under "Upload API & Intake" → "Upload Plan Clarifications" (2026-05-15T22:48:25+02:00 entry). Inbox file deleted. Status: **Formalized and ready for implementation team review gate.** 

Cross-team notification: History updated for Scribe, Nate, and implementation contacts (Eliot/backend).

## 2026-05-16T08:57:46.959+02:00: Issue Bodies Updated with Plan Content

**Session:** Nate

Updated GitHub issue bodies with plan content to close out documentation gap:

- **Issue #11** (Upload API + encrypted file intake): Body replaced with full content from `ai-plans/0011-upload-api-and-encrypted-file-intake.md`. Title preserved. Rationale, all acceptance criteria, and technical details now visible on issue page for implementation team.

- **Issue #12** (Local blob storage + metadata persistence): Body replaced with full content from `ai-plans/0012-local-blob-storage-and-metadata-persistence.md`. Title preserved. Includes intentional deferral note on blob-level integrity verification.

**Rationale:** Issue bodies serve as the contract for implementation; linking them to plans ensures team always has the authoritative scope in one place. Reduces context-switching and prevents plan divergence during review gate.

**Verified:** Both issues show updated bodies via `gh issue view` with plan content intact and titles preserved. Ready for implementation team to reference during coding and PR reviews.

## 2026-05-16T09:31:13.101+02:00: Share-Creation Plan — Eight Review Suggestions Applied

**Session:** Nate

Applied eight substantive refinements to `ai-plans/0013-share-creation-expiration-and-hashed-bearer-tokens.md` based on accepted Nate and Alec review feedback:

1. **Token entropy floor specified:** Both share tokens and optional download bearer tokens must have minimum 256 bits of cryptographic entropy (32 bytes of random data). Acceptance criterion now binding for implementation.

2. **Plaintext token confidentiality enforced:** Added explicit criteria that plaintext tokens are returned only once at creation time and must never be persisted, logged, or included in any server-side record thereafter. Prevents accidental exposure or trace leaks.

3. **Expiration validation deferred:** Clarified that expiration checking (validation at token use-time) belongs entirely to later slices. This slice only persists expiration timestamps as metadata. Prevents scope creep into download/validation endpoints.

4. **Revocation and cleanup fields initialized:** Added acceptance criterion that revocation timestamp (nullable) and cleanup state flag (false at creation) are persisted at share creation time, establishing the foundation for later revocation and cleanup endpoints without implementing them here.

5. **Invalid mode/token combinations spelled out:** Explicitly rejected two invalid configurations with clear rationale:
   - Direct HTTP mode + optional bearer token (invalid: no authentication in direct mode)
   - Separate-key mode without bearer token setup (invalid: separate-key requires optional token configuration)

6. **Error response safety tightened:** Clarified that error responses for invalid mode/token combinations must use generic HTTP 400 with minimal public message, never exposing constraint logic or token-shape details. Prevents attacker inference.

7. **Atomic persistence required:** Added binding criterion that share metadata, file entries, and token hashes must persist atomically; partial failures in any layer trigger full rollback with no orphaned state. Matches cross-layer rollback pattern from upload plan.

8. **Scope boundaries reinforced:** Explicitly stated that this slice does not implement revocation endpoints, background cleanup jobs, download endpoint, or download token validation. All belong to later slices.

**Decision:** Formalized in `decisions.md` under "Share Creation, Expiration & Hashed Bearer Tokens" section. Acceptance criteria now binding for implementation team. Review gate (Nate + Parker, escalate Alec for token/security) applies on PR.

**Status:** Cross-agent context delivered to Alec, Eliot, and Parker via orchestration logs and history updates. Plan ready for backend implementation.
