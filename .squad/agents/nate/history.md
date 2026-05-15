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
