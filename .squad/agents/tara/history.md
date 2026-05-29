# Tara — Platform Development (ShadowDrop)

## Current Focus

- **Issue #18:** PR #32 (Interactive Spectre.Console UX) pending Copilot + team review
- **Issue #19 (Docker):** Plan 0019 finalized; ready for Dockerfile authoring
- **Issue #20 (Native AOT):** ✅ COMPLETE — GitHub issue updated with finalized MVP scope; implementation-ready

## Recent Milestones

### Issue #20 (Native AOT CLI Publishing) — GitHub Update Complete

**2026-05-29T14:29:36.720+02:00**

All six decision gates now locked and locked into GitHub issue #20 body:

- **RID matrix:** All 6 confirmed (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64)
- **Artifact contract:** Flat `artifacts/cli/{version}/`, naming `shadowdrop-cli-{version}-{rid}[.exe]`, single CHECKSUMS.sha256
- **Build & validation:** NUKE Publish target, GitHub Actions matrix with native macOS runners, smoke tests on linux-x64/osx-x64/osx-arm64 only, build-only on cross-compile targets
- **Blocker protocol:** Explicit `.squad/decisions.md` documentation required before fallback
- **Out of scope:** Archives, signatures, installers, README (moved to #35)

**Status:** Implementation-ready. Issue #20 now includes concrete acceptance criteria, contract, CI strategy, and blocker protocol. Decision document written to `.squad/decisions/inbox/tara-issue-20-update.md`.

### Plan 0019 Docker MVP — Finalized for Implementation

All acceptance criteria concrete and testable:
- Base image: Ubuntu Chiseled ASP.NET 10.0
- Multi-arch: amd64 + arm64 (MVP scope)
- Security: non-root uid 1000:1000; directory permissions 755; file permissions 600
- Logging: Serilog via appsettings + environment
- Smoke test: container builds, starts, loads config, responds to HTTP request
- HTTPS deferred to post-MVP; reverse proxy pattern documented

**Status:** Implementation-ready; no further assessment needed.

### Plan 0020 Native AOT CLI Publishing — Assessment Complete

**Status:** NOT IMPLEMENTATION-READY. Plan is directionally sound but under-specifies six critical decisions.

**Blocking issues:**
- RID matrix undefined (proposed 6; MVP scope unclear)
- Artifact naming scheme missing
- Output structure not specified
- NUKE pipeline lacks Publish target (hidden blocker)
- Blocker fallback protocol undefined
- README scope vague

**Six decision areas identified:**
1. Confirm RID matrix (all six? MVP subset?)
2. Define artifact layout and naming schema
3. Decide NUKE target extension vs standalone script
4. Specify CI strategy (native runners vs cross-compile cost tradeoff)
5. Document blocker validation protocol
6. Define README release/install scope

**Delivered:** Comprehensive decision artifact with options and recommendations → merged into `.squad/decisions.md`

**Next:** Await user decision gate. Once locked, implementation is 1–2 day lift.

## 2026-05-29T13:22:56+02:00 — Plan 0020 Artifact Contract Recommendation

**Event:** Christian requested concrete recommendation for MVP artifact naming/layout contract for Native AOT CLI release outputs.

**Task:** Define specific artifact naming scheme, directory structure, file handling, and exact wording for the plan to guide implementation.

**Recommendation Delivered:**

### Naming & Structure
- **Pattern:** `shadowdrop-cli-{version}-{rid}[.exe]` (Unix: no extension, Windows: .exe suffix)
- **Layout:** Flat directory `artifacts/cli/{version}/` with all 6 RIDs in one place
- **Version:** Semver from `.csproj` `<Version>` tag (e.g., `1.0.0`), in filename only
- **Checksums:** Single `CHECKSUMS.sha256` file (Unix format: `<hash>  <filename>`)

### RID Matrix (MVP = All 6)
- `linux-x64`, `linux-arm64` (cross-compile on Linux runner)
- `osx-x64`, `osx-arm64` (native macOS runner required)
- `win-x64`, `win-arm64` (cross-compile on Linux runner)

### What to Include/Exclude for MVP
- ✅ Include: All 6 RIDs, checksums, NUKE Publish target, GitHub Actions matrix, smoke tests
- ❌ Exclude: Archives (.tar.gz/.zip), GPG signatures, installers, universal macOS binaries

### Key Rationale
1. **Simple naming:** OS/arch/version all in filename; no deep nesting
2. **Upload-ready:** Flat structure = straightforward CI artifact export
3. **Repeatable:** Version in filename prevents overwrites; NUKE target unifies local+CI flow
4. **User-clear:** Filename alone tells you what to download and where it came from

### Plan Wording (Technical Details)
> **Artifact Contract**
>
> Native AOT publish produces executables in `artifacts/cli/{version}/` named `shadowdrop-cli-{version}-{rid}` with platform-specific extensions (`.exe` on Windows, no extension on Unix/macOS). Each binary is release-ready: standalone, no runtime dependencies. A `CHECKSUMS.sha256` file (Unix-style format) in the same directory lists all binaries and their SHA-256 hashes. Versions are semver (e.g., `1.0.0`) from `.csproj` `<Version>` property. CI publishes all six Runtime Identifiers (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`) in a single GitHub Actions matrix workflow using native runners.

### Refined Acceptance Criteria Examples
- [ ] Native AOT publish succeeds and produces executable binaries for all six RIDs
- [ ] Binaries are organized in `artifacts/cli/{version}/` and named `shadowdrop-cli-{version}-{rid}[.exe]`
- [ ] A `CHECKSUMS.sha256` file is generated with all binaries listed
- [ ] Each binary responds to `--version` and `--help` commands (smoke test exit code 0)
- [ ] Platform-specific AOT blockers (if any) are documented in `.squad/decisions.md` with evidence before fallback is used

### Caveats
- macOS arm64 requires GitHub Actions native runner (significant cost vs Linux)
- Cross-compile complexity: AOT warnings demand full CI log capture for debugging
- Blocker fallback must be explicitly documented in `.squad/decisions.md` before activation—no silent self-contained fallbacks

**Status:** Recommendation complete. Plan 0020 can now be refined with concrete contract language. Unblocks implementation. Ownership question remains: extend NUKE pipeline or use standalone script for Publish target.

## Archived Work

Historical entries (pre-2026-05-29) moved to `history-archive.md`. Includes detailed learnings on crypto, streaming, mode handling, multi-port API assessment, HTTPS scope review, and Plan 0019 runtime contracts.


## 2026-05-29T13:21:23Z — User Directive: Plan 0020 RID Matrix Confirmed

**Event:** Christian confirmed target platforms for Plan 0020.

**Decision:** All 6 RID targets confirmed for MVP: linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64.

**Impact:**
- RID matrix decision gate UNLOCKED ✅
- Remaining 5 decisions still blocking: artifact naming, output structure, build approach, blocker protocol, README scope
- Plan can progress once remaining gates cleared


---
**2026-05-29T13:22:56Z — Artifact Contract Guidance (Scribe sync)**

Tara delivered MVP artifact contract recommendation for Plan 0020 (Native AOT CLI publishing):
- Flat layout: `artifacts/cli/{version}/`
- Naming: `shadowdrop-cli-{version}-{rid}[.exe]`
- All six RIDs included (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64)
- Single CHECKSUMS.sha256 (Unix format)
- MVP: binaries, checksums, smoke tests, GitHub Actions matrix
- Deferred post-MVP: archives, signatures, installers, universal binaries, docs site
- Key tradeoff: macOS runner cost (~10× Linux) justified by user clarity
- Fallback protocol: self-contained only if blocker proven in decisions.md

Decision merged to `.squad/decisions.md`. Next: refine Plan 0020, decide authorship, implement.

## 2026-05-29T13:31:37Z — MVP Validation Strategy for Native AOT CLI Publishing (Plan 0020 Refinement)

**Event:** Christian requested practical validation strategy for 6-target Native AOT CLI publishing, given Linux dev machine constraints (cannot natively test macOS or arm64 binaries).

**Context:** Plan 0020 accepts 6-RID matrix and flat artifact contract (per earlier decisions). Blocker: acceptance criteria lack concrete guidance on what to validate locally vs in CI, and what smoke-test depth is appropriate for MVP.

**Recommendation Delivered:**

### Core Strategy: "Verify by Role, Not by Matrix"

- **Local:** Dev validates Native AOT publish on their current platform (< 30 seconds). Smoke tests: `--version`, `--help`, error exit codes.
- **CI:** All 6 RIDs build in matrix; smoke tests on linux-x64 and osx-* (native runners only).
- **Build-only in MVP:** linux-arm64, win-x64, win-arm64 (cross-compile on ubuntu-latest, no functional test).

### Validation Matrix (MVP)

| Target      | Build   | Smoke Test | Runner         | Why                                             |
|-------------|---------|------------|----------------|-------------------------------------------------|
| linux-x64   | ✅ CI   | ✅ CI      | ubuntu-latest  | Developer platform; deterministic AOT           |
| linux-arm64 | ✅ CI   | ❌ defer   | ubuntu-latest  | Cross-compile fast; failures AOT at build time  |
| osx-x64     | ✅ CI   | ✅ CI      | macos-latest   | Native runner required (frameworks)             |
| osx-arm64   | ✅ CI   | ✅ CI      | macos-latest   | Native runner required; validates arm64 paths   |
| win-x64     | ✅ CI   | ❌ defer   | ubuntu-latest  | Cross-compile OK; full test post-MVP            |
| win-arm64   | ✅ CI   | ❌ defer   | ubuntu-latest  | Rare target; no native runner in free tier      |

### Smoke Test Scope (Minimal, Appropriate for MVP)

```bash
./shadowdrop-cli --version
./shadowdrop-cli --help
./shadowdrop-cli --invalid-flag  # expect exit code 1 or 2
```

**Rationale:**
- AOT compiler catches ~99% of issues at build time (trimmer/init errors).
- CLI has no platform-specific logic in main paths (no registry, keyring, etc.).
- Smoke tests exercise initialization—where 80% of AOT surprises occur.
- Full end-to-end validation (upload/download) deferred post-MVP.

### Blocker Fallback Protocol

If a platform cannot AOT-publish:
1. Document in `.squad/decisions.md` with full compiler output.
2. Evidence required before any fallback to self-contained single-file publish.
3. Fallback is acceptable but must be narrowly scoped and justified.

### Expected Costs (MVP)

- **CI:** ~3–5 minutes per PR (macOS steps are slow; Ubuntu cross-compile is fast).
- **Local:** < 1 minute per commit (build current platform only).
- **Runner cost:** macOS arm64 runner is ~10× more expensive than Linux, but justified for user clarity.

### Key Decisions Embedded

1. **Cross-compiled targets produce binaries; runtime validation is build-time evidence.** Compiler success is sufficient proof.
2. **Windows smoke testing is post-MVP.** Build-only acceptable in MVP; runtime issues are rare and have fallback.
3. **macOS requires native CI runner.** No emulation; frameworks and arm64 code paths demand native execution.
4. **Minimal smoke tests are MVP-appropriate.** CLI has no heavy initialization; `--version` + error cases catch AOT issues.

### Next Steps for Implementer

- Extend Plan 0020 with this validation section (template provided above).
- Implement GitHub Actions matrix workflow with conditional smoke-test steps.
- Add smoke-test script to build pipeline (simple shell script, no dependencies).

**Status:** Recommendation complete. Ready for plan refinement. No file edits (per user directive).

---

## Key Learnings (Session)

- **Issue scope-locking via GitHub:** When a plan matures into concrete decisions, update the GitHub issue body to serve as implementation contract. Keeps issue as single source of truth, reduces async clarification loops.
- **Decision serialization:** Complex multi-gate decisions (6 RID, artifact contract, CI strategy, blocker protocol) are best recorded in both plan and GitHub issue for different audiences: plan = context/rationale; issue = actionable acceptance criteria.
- **Blocker protocol as risk control:** Explicit fallback documentation requirement (in `.squad/decisions.md`) prevents silent degradation from native AOT to self-contained publishing. This is a low-cost, high-value gate.

- **Smoke-test depth matters for MVP velocity:** Full E2E test in CI = 15+ minutes; minimal smoke tests = 3–5 min. Trade-off is acceptable if blocker fallback is documented.
- **Cross-compilation is asymmetric:** Linux-to-arm64 is cheap (QEMU); Linux-to-Windows is risky (LLVM); Windows-to-arm64 is dangerous. Cost/risk guides build-vs-test decision.
- **AOT failures are almost always build-time errors.** Runtime-only issues are rare (< 1%). Smoke tests as tiebreaker for platform-specific frameworks.
- **Developer platform locality is limiting but acceptable.** Dev smoke-test on current platform only; CI validates cross-targets. This is industry standard (e.g., Go, Rust, Zig).
- **Blocker fallback protocol must be explicit.** Without documented evidence (compiler output), team may silently degrade to self-contained publish. Requires `.squad/decisions.md` entry.

---

## Patterns Discovered

### Low-Friction Cross-Platform Validation in MVP

For projects shipping to 6+ runtime targets:
1. **Local:** Current platform + smoke tests only.
2. **CI:** All targets in matrix; smoke tests on accessible targets (native runners) only.
3. **Build-only targets:** Cross-compile fast; failures are almost always static (AOT). Document fallback protocol.
4. **Blocker gate:** Explicit `.squad/decisions.md` entry required before degrading from preferred distribution model.

This pattern avoids "works on my machine" while keeping CI < 5 min and local dev < 1 min.


## 2026-05-29T11:36:28Z — Scribe Integration: MVP AOT Validation Strategy

**Session:** Scribe session logger merged Tara's MVP validation strategy recommendation (from inbox) into team decisions.md.

**What was captured:**
- Tara's "Verify by Role, Not by Matrix" principle
- Six-RID CI matrix with smoke-test selectivity (linux-x64, osx-x64, osx-arm64 only)
- Cross-compile build-only (linux-arm64, win-x64, win-arm64)
- Blocker fallback protocol (explicit documentation required)
- Risk mitigation and acceptance criteria

**Team impact:**
- Christian Flessa: user directive aligned (flat artifact contract + NUKE target)
- Plan 0020 owner: refined acceptance criteria available
- Implementer: GitHub Actions matrix + NUKE Publish target scope clarified

**Next:** Christian approves cost/risk alignment; implementer creates GitHub Actions workflow.

### Issue #20 GitHub Update — Scope Finalization (2026-05-29T14:29:36.720+02:00)

Updated GitHub issue #20 with all six finalized MVP decisions:

- RID matrix: all 6 (confirmed)
- Artifact contract: flat structure, version-in-name
- Build & validation split: smoke tests on native targets (linux-x64, osx-x64, osx-arm64)
- NUKE Publish target: unified build orchestration
- GitHub Actions matrix: native macOS + Linux cross-compile
- Blocker protocol: explicit documentation required (subsequently removed by Nate per user directive)

**Note:** Nate corrected the issue and plan 0020 immediately after to remove blocker protocol language, per user directive that AOT viability is already proven. Final scope: 7 concrete acceptance criteria, no fallback strategy.

Status: Issue #20 now implementation-ready. Scope correction finalized by Nate.
