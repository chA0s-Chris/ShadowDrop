# Tara — Platform Development (ShadowDrop)

## Learnings (Core Technical)

- **Crypto hot paths:** Use `Guid.TryWriteBytes` for AAD/HKDF buffers (no per-call heap); `EncryptedChunk` mirrors `FileEncryptionContext` boundary (public copy, internal span).
- **Download streaming:** CLI response headers must parse with `NumberStyles.None` + `CultureInfo.InvariantCulture`; JSON metadata requires sanitized filenames (reused for both headers); final chunk length math is fail-closed (checked, 1..ChunkSize).
- **Resume sessions:** Seekable destination streams must have `.Length == DurablePlaintextLength` before any seek/HTTP request (fail-closed validation).
- **Mode parameter handling:** Explicit empty/whitespace mode selectors are rejected; `null` means direct HTTP, `cli` means streamed CLI, any blank/unknown is `InvalidRequest`.
- **ASP.NET Core multi-port binding:** Kestrel supports native multi-endpoint configuration via `ASPNETCORE_URLS` or appsettings. No reverse proxy or sidecar needed. Endpoint isolation via conditional `MapGroup()` registration. Zero breaking changes to single-port default.

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

---

## 2026-05-29T10:07:15Z — Multi-Port API Feasibility (Plan 0019, Criterion 4)

**Event:** Christian requested assessment of dual-port API architecture: Download API on one port, Admin API on separate port, with configurable default.

**Task:** Evaluate from platform/runtime perspective:
- Can .NET web app listen on two ports with endpoint isolation?
- What hosting approach is cleanest?
- Does this add MVP friction?
- Best wording for acceptance criteria?

**Findings:**
- **Feasible and low-risk.** ASP.NET Core 10 natively supports multi-port Kestrel binding via `ASPNETCORE_URLS` env var.
- **Code scope:** ~10 lines (extend `ApiExposureOptions` with optional `DownloadPort`/`AdminPort`, add conditional Kestrel setup in `Middleware.cs`).
- **Zero breaking changes:** Default single-port behavior unchanged.
- **Current API structure already ready:** `AdminEndpoints` and `DownloadEndpoints` both have feature-flag guards; just extend with port-specific registration.

**Recommendation:** Include in plan acceptance criteria. Suggested criterion 4 revision:

> The runtime supports optional port splitting via environment configuration: `ASPNETCORE_URLS=http://0.0.0.0:PORT` for single-port default, or multi-endpoint Kestrel binding for separate download and admin ports. Configuration examples document both modes.

**Tradeoffs:** Config complexity ✅ low, code complexity ✅ very low, test surface ⚠️ moderate (two smoke paths), operational flexibility ✅ high (firewall rules, security boundaries).

## 2026-05-29T10:09:06Z — Port-Split Assessment Merged

**Cross-agent note:** Assessment artifact moved from `.squad/decisions/inbox/tara-multiport-api-feasibility.md` into `.squad/decisions.md` as canonical team record. Nate's concurrent assessment recommends deferral from 0019 MVP; Tara recommends inclusion. Divergence logged in orchestration log and session log. Christian to decide final scope for plan 0019.

**Action:** Monitor plan 0019 acceptance criteria refinement for Christian's decision.

## 2026-05-29T12:11:18+02:00 — HTTPS Support for Plan 0019 MVP Assessment

**Event:** Christian requested assessment of whether HTTPS support (TLS termination in app/container) should be in MVP, or rely on reverse proxy.

**Task:** Evaluate certificate management burden, operational fit, and MVP scope impact.

**Findings:**
- **Plain HTTP + reverse proxy is MVP-optimal.** Home lab/small VPS deployments already use or can easily add a reverse proxy (Caddy, nginx, Traefik).
- **Certificate management is a support tax.** App-managed HTTPS requires provisioning, renewal automation, cert path mounting, and substantial docs. Reverse proxies handle this cleanly (Caddy auto-renews).
- **Security best practice:** Reverse proxy sits in front; app listens on private network. Reverse proxy provides bonus features (request logging, rate limiting, auth headers). Adding app-level HTTPS duplicates their effort and adds surface area.
- **Clean post-MVP path:** If users demand direct HTTPS, it's a straightforward ASP.NET Core feature (one acceptance criterion). No architectural debt.

**Recommendation:** 
Defer HTTPS to post-MVP. Document plain HTTP with reverse proxy example in README (Docker Compose with Caddy).

**Decision artifact:** `.squad/decisions/inbox/tara-https-mvp-review.md` — Ready for Christian's review and merge.

---

## Session Log: 2026-05-29T10:13:48Z — Plan 0019 HTTPS Scope Assessment

**Status:** Merged to decisions.md

Assessment from inbox (tara-https-mvp-review.md) merged into `.squad/decisions.md` with detailed certificate management complexity analysis.

**Key contribution:** Framed reverse proxy (Caddy, nginx, Traefik) as standard best practice, not workaround; documented certification renewal/storage burden for app-managed HTTPS.

**Coordination:** Nate (Lead) issued parallel assessment; Christian provided user directive on scope prioritization.

**Actionable output:** Plan 0019 documentation should clarify HTTP-only binding; reverse proxy guidance in README recommended.

## 2026-05-29T12:15:35.979+02:00 — HTTPS Issue Created (#34)

**Event:** Christian requested brief GitHub issue for deferred HTTPS support.

**Task:** Create implementation-friendly issue reflecting that MVP uses HTTP + reverse proxy, with post-MVP HTTPS as optional enhancement.

**Action:** Issue #34 created with context, rationale, and acceptance criteria. Scope clearly frames reverse proxy as best practice (not workaround) and HTTPS as future enhancement with defined entry criteria.

## 2026-05-29T12:25:53.733+02:00 — Plan 0019 Runtime Contract Assessment

**Event:** Christian requested assessment of five open runtime details for Docker image MVP.

**Task:** Evaluate and recommend concrete wording for:
1. Multi-arch contract (amd64+arm64 only)
2. Registry publication scope (skip for MVP)
3. Validation/smoke-test contract (measurable specification)
4. Non-root execution requirement for Chiseled
5. File permissions for `/app/data` (files vs directories)
6. Logging configuration via existing appsettings/environment

**Findings:**
- ✅ All five details are sound MVP choices
- Multi-arch amd64+arm64: Correct (covers 95%+ of deployments, tight matrix for reliability)
- Skip registry publication: Sound (no build burden, local Docker workflows sufficient)
- Smoke test: Needs concrete spec (proposed: shell script with health check, config validation, non-root verification)
- Non-root enforcement: Should be explicit (Chiseled security model, `USER 1000` in Dockerfile)
- File permissions: `600` for files correct; distinguish directories (755) from files (600)
- Logging: Correct design (existing Serilog integration, appsettings+env override, custom mount support)

**Deliverable:** Decision document `.squad/decisions/inbox/tara-runtime-contract-review.md` with:
- Per-point rationale and findings
- Consolidated acceptance criteria wording for all six criteria
- Validation/smoke-test concrete specification
- File permission guidance (distinguished by resource type)

**Impact:** Plan 0019 acceptance criteria now have measurable, platform-aligned language. No code work required until Christian reviews and merges.


## 2026-05-29T10:44:50Z — Plan 0019 Ready for Dockerfile Implementation

**Scribe update (cross-agent context):** Plan 0019 (Docker image and container deployment) MVP scope is now finalized and implementation-ready. Your platform assessment (runtime contract review) has been recorded in team decisions. All acceptance criteria are concrete, testable, and include:

- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`
- Single port: `19423` (both public download routes and admin routes)
- Paths: `/app/data/shadowdrop.db`, `/app/data/blobs/`
- Security: uid/gid `1000:1000`, dir permissions `700`, file permissions `600`
- Multi-arch support: amd64 + arm64 only (MVP scope)
- Config: environment variables (Serilog log levels), custom `appsettings.json` mount support
- Smoke test: container builds, starts, loads config, responds to HTTP request
- HTTPS: deferred to post-MVP; reverse-proxy pattern documented

**Next:** You can begin Dockerfile authoring immediately. Acceptance criteria are unambiguous and actionable. Smoke test definition is concrete (single-container validation, no external dependencies).

**Deferred:** Issue #33 tracks optional two-port mode (separate public/admin routes); not MVP.
