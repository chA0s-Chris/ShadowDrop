---
name: "crypto-buffer-encapsulation"
description: "Keep secret-adjacent byte arrays immutable at the public boundary while preserving zero-copy internal crypto paths"
domain: "security, api-design"
confidence: "high"
source: "earned"
---

## Context

Use this pattern when a public API exposes cryptographic context or key-derivation inputs as `byte[]`. Even when the data is non-secret, mutable array aliases let callers silently rewrite trust-boundary state after validation.

## Patterns

- Copy inbound `byte[]` values at construction time before storing them.
- Return a fresh `byte[]` copy from public getters so callers cannot mutate internal state.
- Expose an internal `ReadOnlySpan<byte>` or equivalent for hot-path cryptographic operations to avoid extra heap churn inside the same assembly.
- When a derivation step needs a temporary heap buffer, zero it in a `finally` block after ownership has been safely transferred or copied.

## Examples

- `src/ShadowDrop.Shared/Crypto/FileEncryptionContext.cs`: stores `_kdfSalt`, returns `KdfSalt => _kdfSalt.ToArray()`, and provides `internal ReadOnlySpan<byte> KdfSaltBytes` for derivation.
- `src/ShadowDrop.Shared/Crypto/EncryptedChunk.cs`: stores `_ciphertext`, exposes `Ciphertext => _ciphertext.ToArray()`, and keeps `internal ReadOnlySpan<byte> CiphertextBytes` for decrypt/encrypt consumers in the same assembly.
- `src/ShadowDrop.Shared/Crypto/ChunkEncryptionService.cs`: wraps HKDF output in `try/finally` and clears the temporary buffer with `CryptographicOperations.ZeroMemory`.

## Anti-Patterns

- Returning a stored `byte[]` directly from a public property.
- Generating security-relevant context objects with hidden random values that bypass documented lifecycle rules.
- Leaving temporary derived-key buffers uncleared after copying them into a managed wrapper.
