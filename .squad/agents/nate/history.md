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
