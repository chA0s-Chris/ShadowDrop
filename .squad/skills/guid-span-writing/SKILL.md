---
name: "guid-span-writing"
description: "Write Guid values directly into spans on hot paths instead of allocating temporary byte arrays"
domain: "performance, crypto"
confidence: "high"
source: "earned"
---

## Context

Use this when code already assembles protocol, crypto, or serialization payloads in a `Span<byte>`. Calling `Guid.ToByteArray()` inside that path defeats the point by allocating a fresh 16-byte array per call.

## Patterns

- Prefer `Guid.TryWriteBytes(destinationSpan)` when the destination buffer already exists.
- Hide the write behind a small helper when the same pattern appears multiple times in one file.
- Keep regression tests focused on semantics around the affected payload builder rather than trying to assert raw allocation counts in brittle unit tests.

## Examples

- `src/ShadowDrop.Shared/Crypto/ChunkEncryptionService.cs`: `WriteGuid` now uses `Guid.TryWriteBytes` for both AAD and HKDF info construction.
- `tests/ShadowDrop.Shared.Tests/Crypto/ChunkEncryptionServiceTests.cs`: context-mismatch coverage ensures GUID-backed derivation inputs still affect key material correctly.

## Anti-Patterns

- Calling `Guid.ToByteArray()` inside loops or per-request/per-chunk payload builders.
- Adding stackalloc/span plumbing and then reintroducing heap churn with temporary GUID arrays.
