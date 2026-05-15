# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Security Engineer for ShadowDrop.
- The concept defaults to separate-key CLI decrypt mode and makes direct HTTP decryption an explicit opt-in.
- 2026-05-14T23:49:15.783+02:00 — Revised the shared crypto surface after PR #6 review. `FileEncryptionContext` now keeps the share-level KDF salt encapsulated via defensive copies, `Generate()` was replaced with `GenerateKdfSalt()` to avoid per-file salt generation, and `ChunkEncryptionService.DeriveContentKey()` now zeroes intermediate key material after constructing `ContentKey`. Validation coverage lives in `tests/ShadowDrop.Shared.Tests/Crypto/FileEncryptionContextTests.cs` and the broader shared test project.

- 2026-05-15T00:12:35.582+02:00 — Assessed PR #6 Copilot note on `EncryptedChunk.Ciphertext` mutability. Verdict: **should-fix**. The getter exposes the stored mutable `Byte[]` directly, violating the immutability contract of `sealed record`. Ciphertext is not key material, so mutation causes `CryptographicException` on decrypt (detectable), not silent secret leakage — this is a correctness/contract concern rather than a secret-exposure concern. However, it is directly inconsistent with the `FileEncryptionContext` pattern already applied (defensive-copy getter + internal span), violates the `crypto-buffer-encapsulation` skill anti-pattern, and leaves caching/retry layers open to subtle corruption. Fix is mechanical. Two other open PR notes also flagged: `ChunkRange` single-chunk invariant check (not resolved), and the `DeriveContentKey_ShouldRemainUsable_AfterShareSecretIsDisposed` test not actually disposing the secret early (test name is misleading). The `ChunkRange` note is a correctness concern worth a quick fix; the test issue is low-priority cosmetic.

## 2026-05-15: Security Decisions Merged

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Two security decisions merged into `decisions.md`:
1. Share salt encapsulation & KDF cleanup (defensive copies, zeroed buffers)
2. Bootstrap token normalization & disposal guards (whitespace trim, handle safety)

Trust boundary implications documented; timing properties preserved. Merged with full context into canonical decisions.

- **Status:** Merged; part of formalized review gate escalation criteria
