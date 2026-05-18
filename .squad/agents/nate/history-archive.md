# Nate — History Archive (Pre-2026-05-16)

## PRs and Code Reviews (2026-05-14 to 2026-05-15)

- PR #6 crypto design review: KDF salt mutability, per-file design violations, key material cleanup issues identified and fixed by Tara
- PR #6 follow-up assessment: All three Copilot notes resolved (ChunkRange validation, EncryptedChunk defensiveness, dispose-before-crypto test)
- PR #6 final review: Targeted test run (51 tests) passed. PR ready for merge.

## Issue #2 Design Review (2026-05-14)

- Settled crypto design: deterministic nonce from chunk index, 50-byte AAD, HKDF-SHA-256, ChunkEncryptionService sealed class
- Branch created: `squad/2-chunked-aes-gcm-crypto-spike`
- Test approach: NUnit 4 + FluentAssertions, sociable unit tests, no mocking framework

