# Squad Decisions

## Active Decisions

- 2026-05-14: The initial ShadowDrop squad uses the user-chosen names Nate, Eliot, Sophie, Alec, Tara, and Parker, with Scribe and Ralph as built-in roles.
- 2026-05-14: ShadowDrop work is routed by specialization across lead, backend, CLI, security, platform, and testing rather than a generic pooled roster.

## Dependency & AOT Strategy

- 2026-05-14: Keep baseline dependency wiring aligned to deployment boundaries; make Native AOT support explicit in CLI project from first slice (Tara #1). Applied in Directory.Packages.props, ShadowDrop.Api.csproj, ShadowDrop.Cli.csproj. Ensures minimal API/CLI package surface, preserves shared-library boundary, enables `linux-x64` publish path for local and CI before feature code accumulates.

## Encryption & Cryptographic Design

- 2026-05-14 (Nate, Issue #2): Chunked AES-256-GCM crypto with deterministic nonces from chunk index, HKDF-SHA-256 per-file key derivation, sealed `ChunkEncryptionService` API. Test surfaces: 26 cases covering round-trip, range mapping, and tamper detection. AOT-compatible. See `.squad/decisions/inbox/nate-issue-2-crypto-design.md` for full details.

## Platform & Release Management

- 2026-05-14 (Tara, Issue #1): PR #5 targets `main` for foundational wiring (Directory.Packages.props, project refs, baseline builds). Rationale: no `dev` integration branch exists; stable baseline is essential before feature work.

## Team Policy

- 2026-05-14 (Copilot directive, Christian Flessa): Automatic commits allowed only for `./.squad/` changes. All commits must use Conventional Commits format; squad commits must use `docs(squad):` type prefix. Never push commits.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

### 2026-05-14T22:52:54.277+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Do not commit codebase changes unless explicitly asked; it is okay to commit squad-related changes inside `./.squad/`; do not push any commit to the remote; do not create a PR unless explicitly asked.
**Why:** User request — captured for team memory
