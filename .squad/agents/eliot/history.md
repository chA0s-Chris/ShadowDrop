# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Backend Dev for ShadowDrop.
- The MVP centers on upload, share, download, revoke, and cleanup workflows.

## Cross-Agent Updates — 2026-05-14T18:43:13Z

**From Nate (Issue #2 Crypto Spike):**  
Crypto design finalized. Handoff: implement production code in `src/ShadowDrop.Shared/Crypto/` per API spec in `.squad/decisions.md` (Encryption section). Key constraints: deterministic nonces from chunk index, HKDF-SHA-256 key derivation, 50-byte AAD binding, `stackalloc` for AAD, `IDisposable` + `CryptographicOperations.ZeroMemory` on key types. All AOT-safe (System.Security.Cryptography.HKDF, AesGcm). Parker will test to 26 cases. Branch `squad/2-chunked-aes-gcm-crypto-spike` ready for implementation.
