# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Security Engineer for ShadowDrop.
- The concept defaults to separate-key CLI decrypt mode and makes direct HTTP decryption an explicit opt-in.
- 2026-05-14T23:49:15.783+02:00 — Revised the shared crypto surface after PR #6 review. `FileEncryptionContext` now keeps the share-level KDF salt encapsulated via defensive copies, `Generate()` was replaced with `GenerateKdfSalt()` to avoid per-file salt generation, and `ChunkEncryptionService.DeriveContentKey()` now zeroes intermediate key material after constructing `ContentKey`. Validation coverage lives in `tests/ShadowDrop.Shared.Tests/Crypto/FileEncryptionContextTests.cs` and the broader shared test project.
