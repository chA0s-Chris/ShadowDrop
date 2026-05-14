# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Tester for ShadowDrop.
- The concept puts special emphasis on resumable downloads, token expiry, revocation, and multi-file share behavior.

## Cross-Agent Updates — 2026-05-14T18:43:13Z

**From Nate (Issue #2 Crypto Spike):**  
Crypto spike complete. Handoff: implement 26 test cases in `tests/ShadowDrop.Shared.Tests/Crypto/` per spec in `.squad/decisions.md` (Encryption section). Happy-path: 14 cases (single/multi-chunk round-trips, range alignment, sub-chunk). Failure-path: 12 cases (tamper ciphertext, metadata fields, wrong key/context). Use NUnit 4 + FluentAssertions. No mocks. Branch `squad/2-chunked-aes-gcm-crypto-spike` ready.
