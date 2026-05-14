# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Platform Dev for ShadowDrop.
- The concept requires one-container Docker distribution plus x64 and arm64 delivery for server and CLI artifacts.
- 2026-05-14T20:06:45.536+02:00: `ShadowDrop.slnx` already contains the three production projects plus all three test projects, so issue work should preserve existing solution membership instead of recreating it.
- 2026-05-14T20:06:45.536+02:00: Baseline dependency wiring is centralized in `Directory.Packages.props`; `LiteDB` is scoped to `src/ShadowDrop.Api/ShadowDrop.Api.csproj`, while `System.CommandLine` and `Spectre.Console` are scoped to `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj`.
- 2026-05-14T20:06:45.536+02:00: Native AOT visibility for the CLI lives in `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj` via `IsAotCompatible`, `PublishAot`, and `InvariantGlobalization`, with `linux-x64` smoke publish as the baseline validation path.
- 2026-05-14T20:21:46.760+02:00: Created PR #5 for issue #1 targeting `main` (no `dev` branch in this repo). PR body mirrors acceptance criteria with validation checklist; linked and closes issue #1.
- 2026-05-14T22:55:52.308+02:00: For local history surgery on `squad/2-chunked-aes-gcm-crypto-spike`, the safe path was to rebase the trailing `.squad/` docs commit onto `afb29a7` and then restore `src/ShadowDrop.Shared/Crypto/*` plus `tests/ShadowDrop.Shared.Tests/Crypto/*` with `git cherry-pick --no-commit`, leaving the code/test changes staged for review while removing commits `ff9b8ff` and `546e543` from branch history.
- 2026-05-15T00:01:13.117+02:00: The crypto hot path in `src/ShadowDrop.Shared/Crypto/ChunkEncryptionService.cs` serializes `Guid` values into stackalloc buffers for AAD/HKDF info; use `Guid.TryWriteBytes` instead of `Guid.ToByteArray()` there to avoid per-call heap allocations. Regression coverage for derived-key context mismatches lives in `tests/ShadowDrop.Shared.Tests/Crypto/ChunkEncryptionServiceTests.cs`.
- 2026-05-15T00:16:55.489+02:00: `src/ShadowDrop.Shared/Crypto/EncryptedChunk.cs` should follow the same buffer-encapsulation pattern as `FileEncryptionContext`: public `byte[]` access returns a copy, while `ChunkEncryptionService` consumes an internal `ReadOnlySpan<byte>` so encrypt/decrypt stay zero-copy inside the assembly. Single-chunk invariants for `ChunkRange` are regression-covered in `tests/ShadowDrop.Shared.Tests/Crypto/ChunkRangeMappingTests.cs`.

## Squad Transition — 2026-05-14T18:13:02Z

Issue #1 decision (dependency & AOT strategy) merged to team decisions by Scribe.
