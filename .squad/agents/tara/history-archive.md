# Tara — Platform Development (ShadowDrop) — Archive

Older entries from history.md (pre-2026-05-29T11:14).

## Core Learnings Archive

**Crypto hot paths, download streaming, resume sessions, mode parameter handling, ASP.NET multi-port binding:**
- Use `Guid.TryWriteBytes` for AAD/HKDF buffers (no per-call heap)
- CLI response headers must parse with `NumberStyles.None` + `CultureInfo.InvariantCulture`
- Seekable destination streams must have `.Length == DurablePlaintextLength` before any seek
- Explicit empty/whitespace mode selectors rejected; `null` means direct HTTP, `cli` means streamed CLI
- ASP.NET Core supports native multi-endpoint Kestrel configuration via `ASPNETCORE_URLS`

## Delivered Work (2026-05-19 through 2026-05-29)

7 hardening fixes merged:
1. Invalid mode overload fail-closed fix
2. Strict CLI header parsing (invariant culture)
3. Resume destination length validation
4. Bearer-token test signature repair
5. Filename sanitization consolidation
6. Streamed metadata header safety
7. Chunk corruption detection (fail-closed)

All PRs reviewed and approved by Parker.

## Historical Assessments

- **Plan 0019 Docker:** Ubuntu Chiseled ASP.NET evaluation completed 2026-05-29T09:54:40Z
- **Multi-Port API:** Feasibility assessment 2026-05-29T10:07:15Z; NUKE extension ~10 lines; low-risk
- **Plan 0019 HTTPS:** Assessment 2026-05-29T12:11:18Z; deferred post-MVP; reverse proxy pattern documented
- **Plan 0019 Runtime Contract:** Five runtime details assessed 2026-05-29T12:25:53Z; all sound for MVP
- **Issue #34 (HTTPS):** Created 2026-05-29T12:15:35Z with deferred implementation context
- **Plan 0020 AOT (detailed assessment):** 2026-05-29T13:14:07Z; comprehensive findings logged

All historical assessments merged to team decisions. See `.squad/decisions.md` for canonical records.

