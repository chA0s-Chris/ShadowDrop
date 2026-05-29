# Tara — Platform Development (ShadowDrop)

## Learnings (Core Technical)

- **Crypto hot paths:** Use `Guid.TryWriteBytes` for AAD/HKDF buffers (no per-call heap); `EncryptedChunk` mirrors `FileEncryptionContext` boundary (public copy, internal span).
- **Download streaming:** CLI response headers must parse with `NumberStyles.None` + `CultureInfo.InvariantCulture`; JSON metadata requires sanitized filenames (reused for both headers); final chunk length math is fail-closed (checked, 1..ChunkSize).
- **Resume sessions:** Seekable destination streams must have `.Length == DurablePlaintextLength` before any seek/HTTP request (fail-closed validation).
- **Mode parameter handling:** Explicit empty/whitespace mode selectors are rejected; `null` means direct HTTP, `cli` means streamed CLI, any blank/unknown is `InvalidRequest`.

## Active Work

- **Issue #18 (Interactive Spectre.Console UX):** PR #32 created 2026-05-29T08:23:50Z; pending Copilot + team review.
- **Issue #19 (Docker):** Deferred pending Christian scope clarification.
- **Issue #20 (Native AOT):** Deferred pending Christian scope clarification.
- **Plan 0019 Assessment:** Ubuntu Chiseled ASP.NET evaluation for Docker image strategy; assessment completed 2026-05-29T09:54:40Z, awaiting merge.

## Team Contribution

Delivered 7 critical hardening fixes across 2026-05-19 through 2026-05-29:
1. Invalid mode overload fail-closed fix
2. Strict CLI header parsing (invariant culture)
3. Resume destination length validation
4. Bearer-token test signature repair
5. Filename sanitization consolidation
6. Streamed metadata header safety
7. Chunk corruption detection (fail-closed)

All PRs merged and approved by Parker (review authority).

## Decision Tracking

7 decisions recorded in `.squad/decisions.md` covering crypto allocation, download validation, streaming contracts, and resume safety.

---

## 2026-05-29T09:54:40Z — Plan 0019 Docker Assessment (Ubuntu Chiseled ASP.NET)

**Event:** Scribe logged Tara's assessment task for plan 0019 (Docker image and container deployment).

**Task:** Evaluate Ubuntu Chiseled ASP.NET base image as containerization strategy for ShadowDrop:
- Image footprint and attack surface reduction
- Multi-architecture support feasibility (x64/arm64)
- CI/CD integration assumptions
- Registry and deployment workflow maturity

**Status:** Assessment completed. Output awaiting canonical merge to decisions.md.

**Artifacts:**
- Orchestration log: `.squad/orchestration-log/2026-05-29T09:54:40Z-tara-plan-0019-assessment.md`
- Session log: `.squad/log/2026-05-29T09:54:40Z-chiseled-image-review.md`
