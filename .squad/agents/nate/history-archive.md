# SUMMARY — Generated 2026-05-29T13:51:32.596+02:00

**Coverage:** May 29, 2026 (current session)
**Focus:** Plan 0020 Native AOT CLI Publishing — MVP lock with settled platform contracts
**Status:** Plan 0020 ready for implementation. Flat artifact schema, NUKE Publish target, GitHub Actions workflow, and validation split now concrete. README work moved to issue #35.

## Current Session Highlights

- **Plan 0019 Assessment:** Identified three ambiguities in acceptance criteria (smoke test, volume mount, runtime defaults).
- **Plan 0019 Finalization:** Embedded all five user-directed MVP decisions into concrete, testable criteria. Route separation deferred to issue #33.
- **Plan 0019 Polish:** Applied final wording refinements to smoke test, volume mount requirement, and runtime defaults for implementation clarity.
- **Status:** Zero ambiguous acceptance criteria. Plan ready for Dockerfile authoring by Tara (Platform).

## Key Decisions Embedded in Plan 0019 MVP

- **Base Image:** Ubuntu Chiseled ASP.NET 10.0 (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`)
- **Port:** Single port `19423` (both public downloads and admin routes together)
- **Storage:** `/app/data/shadowdrop.db` (LiteDB), `/app/data/blobs/` (blob storage)
- **Permissions:** `700` for `/app/data`, `600` for database file
- **Non-root Execution:** uid/gid `1000:1000`
- **Multi-arch:** amd64 and arm64 only
- **HTTPS:** None in MVP; reverse-proxy pattern documented
- **Health Check:** None in MVP
- **Smoke Test:** Concrete single-container validation (starts, loads config, responds to request)

---

## Archived Details

### PR #31 Review Cycle (May 29T03:13–07:28)

See `.squad/agents/nate/history-archive.md` for detailed PR #31 reassessment work (three separate triage passes, duplicate fileId defense-in-depth analysis, queue contract documentation validation). **Outcome:** PR #31 merge-ready from correctness standpoint; two optimization suggestions deferred.

### Issue Triage (May 29T09:02)

See history-archive.md for #18/#19/#20 sequencing recommendation. **Outcome:** #18 (Interactive UX) READY NEXT; #19/#20 defer pending scope clarification.

### Pre-Plan 0019 Assessment Work (May 29T09:45–12:25)

See history-archive.md for initial plan assessments, port-split review, HTTPS MVP analysis, and user input interpretation. **Outcome:** All five user inputs sound; issue was wording clarity, not architecture. Plan 0019 becomes implementation-ready after criteria refinement.

---

## Cross-Agent Coordination

- **Sophie (CLI Downloads):** Complementary PR #31 triage; plan 0017 queue contract alignment verified.
- **Tara (Platform):** Concurrent Plan 0019 assessment; multi-arch and permissions recommendations aligned; ready for Dockerfile authoring.
- **Eliot (Backend):** PR #31 review-fix phase; queue serverUrl validation and per-entry streaming completed.
- **Parker (Tester):** Async assertion fixes and test regression coverage for upload flow.

### 2026-05-29T13:00:08.513+02:00: Inbox Merge Complete

**Scribe Action:** User directive on `/health` endpoint and Nate's Docker MVP refinement decision merged into decisions.md. Three targeted Plan 0019 improvements now locked:

1. Smoke test: `GET /health` with HTTP 200 (not generic "existing endpoint").
2. First-start database creation: Explicit acceptance criterion (auto-init LiteDB on first mount).
3. Multi-arch validation: Concrete `docker buildx build --platform linux/amd64,linux/arm64` command in criteria.

**Status:** Plan 0019 MVP refinements archived for team memory. Implementation can proceed with zero ambiguity.

---

## Learnings

- **API Configuration Already Complete:** `ShadowDropOptions` and `ApiExposureOptions` already support all required Plan 0019 patterns. No code gaps.
- **Acceptance Criteria Over Architecture:** All user inputs are sound; implementation blockers are clarity and specificity, not design flaws.
- **Smoke Test Clarity Matters:** "Simple GET request" → concrete validation contract (logs, existing endpoint, API readiness proof) removes implementation guesswork.
- **Explicit Volume Mount Requirement:** Implicit assumptions about Docker mount points lead to persistent data loss. Explicit requirement in criteria is non-negotiable.
- **Runtime Defaults Fully Explicit:** Bind address, port, and env var override options must be stated precisely to prevent deployment surprises.
- **Health Endpoint Specification:** User refined smoke test to use exact `/health` endpoint with HTTP 200, not a generic "existing endpoint". This increases implementer clarity and test reliability.
- **Database First-Start Behavior:** Explicit acceptance criterion for auto-creation of LiteDB schema on first mount ensures users understand data is not lost; deployment guides must document this.
- **Multi-Arch Validation Command:** Concrete `docker buildx` command in acceptance criteria prevents ambiguity about "both amd64 and arm64 support"; implementer has exact validation target.
- **Plan Refinement Pattern:** User-directed MVP refinements focus on concrete, testable specifics—not architectural changes. Three targeted edits to acceptance criteria + technical details preserved scope discipline.

### 2026-05-29T14:29:36.720+02:00: Issue #20 Scope Correction — Fallback Protocol Removal

**Action:** Removed all fallback/blocker-protocol language from plan 0020 and issue #20 per user directive.

**Removed:**

1. Acceptance Criterion 8 about documenting blockers before fallback
2. "Blocker protocol" section from Technical Details
3. Verbose "Blocker Fallback Protocol" section in GitHub issue

**Kept:** All seven concrete implementation criteria (RID matrix, artifact contract, NUKE target, CI matrix, smoke-test split).

**Why:** AOT viability already proven for CLI; fallback strategy is out of scope. Cleaner contracts enable immediate assignment and execution.

**Decision Record:** Written to `.squad/decisions/inbox/nate-issue-20-scope-correction.md`

**Pattern Discovered:** GitHub issue scope creep often stems from **policy language bleeding into acceptance criteria**. When a decision is settled and proven, remove the policy wrapper and keep only the implementation contract. Keeps issues implementation-oriented rather than decision-revisiting.

### 2026-05-29T13:51:32.596+02:00: Plan 0020 MVP Lock — Flat Artifact Contract & Implementation-Ready Criteria

**What:** Embedded all settled MVP decisions from `.squad/decisions.md` into plan 0020, converting from vague acceptance criteria to concrete, testable specifications.

**Decisions incorporated:**

1. **RID Matrix:** All six confirmed (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64).
2. **Artifact naming & layout:** Flat schema `artifacts/cli/{version}/shadowdrop-cli-{version}-{rid}[.exe]` with checksums.
3. **NUKE Publish target:** Dedicated target builds all six executables in one invocation (not manual).
4. **GitHub Actions workflow:** Matrix with native runners for macOS; cross-compile on Linux for arm64/Windows.
5. **Validation split:** Smoke tests on accessible targets (linux-x64, osx-x64, osx-arm64); build-only on cross-compiled (linux-arm64, win-x64, win-arm64).
6. **Blocker protocol:** Removed vague fallback language; now explicit: blockers must be documented in decisions.md with evidence before fallback is considered.
7. **README work removed:** Moved to issue #35 per user directive.

**Acceptance criteria now tight:** Nine specific, testable criteria replacing the original six vague ones. No guessing on file layout, naming, CI/CD integration points, or smoke-test scope.

**Key pattern:** Started with loose "blockers are documented" → refined to "documented in `.squad/decisions.md` with evidence (error output, dependency version, mitigation rationale) **before** fallback considered." This closes the loop on what "proven blocker" means.

**Status:** Plan 0020 now implementation-ready. Sophie or Tara can begin NUKE target + GitHub workflow authoring without re-questioning scope.

### 2026-05-29T13:14:07.984+02:00: Plan 0020 Assessment — Native AOT CLI Publishing

**Status:** NOT IMPLEMENTATION-READY. Five vague acceptance criteria; three critical missing decisions; one hidden dependency (no NUKE Publish target exists).

**Key Findings:**

- Acceptance criteria "organized as release-ready," "naming is consistent," "blockers documented," and "README updated" lack testable definitions or destination specs.
- RID matrix not explicitly stated (six platform/arch combos assumed but not confirmed).
- Artifact output format, directory structure, and file naming scheme all undefined; implementer will guess.
- CI/CD integration point unclear (who writes the NUKE Publish target?).
- Plan assumes "existing Native AOT configuration" but NUKE build pipeline has no Publish target; historical evidence shows manual `dotnet publish -r linux-x64` commands from Parker's work.
- "Blocker fallback" strategy is policy, not acceptance criterion; no trigger defined.
- "Trimming-safe" requirement in Technical Details has no acceptance test.

**Exact Problems:**

1. Plan text "publish outputs organized as release-ready artifacts" gives implementer zero guidance on layout.
2. Plan text "naming is consistent" names no actual scheme (e.g., is it `shadowdrop-cli-linux-x64` or `ShadowDrop.Cli-linux-x64`?).
3. Plan text "publish succeeds" doesn't define test (exit code 0? binary exists? binary runs --help?).
4. Plan text "blockers documented" doesn't specify file path or content.
5. Plan text "README updated" doesn't specify which README or minimum content.

**Blocker Decisions Needed (in decisions.md before implementation):**

- RID matrix explicit (confirm all six or subset).
- Artifact naming and layout scheme (directory structure, file naming, compression).
- Blocker documentation destination (file path).
- Publish target ownership (Sophie/Tara) and NUKE integration point.
- README scope (file path, content checklist).
- Validation harness for "publish succeeds" (manual verification, CI gate, functional test).

**Positive:** Team has proven Native AOT works for linux-x64 (Plan 0001). No architectural risk; only scope/contract clarity needed.

**Recommendation:** Block on user clarification or Nate decision. Once contracts settle, implementation is straightforward (1-2 day lift for appropriate specialist).

## 2026-05-29T11:14:07Z — Plan 0020 Readiness Assessment

**Status:** Assessment complete; plan blocked on contract clarity

**What:** Validated Plan 0020 (Native AOT CLI Publishing) execution readiness. Identified vague acceptance criteria and missing critical decisions that prevent work assignment.

**Blocking issues found:**

1. Five vague acceptance criteria (no layout/naming/success definition/doc paths/scope)
2. Three missing critical decisions (RID matrix, artifact format, CI/CD ownership)
3. One hidden blocker: NUKE pipeline lacks Publish target; current publishes are manual

**Key discovery:** `build/BuildPipeline.Build.cs` only defines Build/Test/Clean/Restore targets. Publish target must be written or delegated to script.

**Decisions needed before assignment:**

1. Confirm RID matrix (assumed 6; or MVP subset?)
2. Define artifact naming scheme with examples
3. Specify artifact output structure (directory layout, compression, checksums?)
4. Decide NUKE target ownership and implementation
5. Specify blocker documentation path
6. Define README scope (installation + version matrix?)
7. Define publish success test (exit 0? binary executable? runs --help?)

**Delivered:** `.squad/decisions/inbox/nate-plan-0020-requires-contract-clarification.md` (blocking issues + required decisions) → merged into `.squad/decisions.md`

**Cross-agent:** Tara (Platform) provided detailed options for all six decision areas. Assessments merged into unified decision record.

**Risk:** Leaving vague means implementer builds artifact scheme then discovers mismatch with downstream release process.

**Next:** User or lead must unlock seven decision gates before implementation starts.

## 2026-05-29T13:21:23Z — User Directive: Plan 0020 RID Matrix Confirmed

**Event:** Christian confirmed target platforms for Plan 0020.

**Decision:** All 6 RID targets confirmed for MVP: linux-x64, linux-arm64, win-x64, win-arm64, osx-x64, osx-arm64.

**Impact:** First of seven decision gates now unlocked. Remaining six gates (artifact naming, output structure, build/publish approach, blocker protocol, README scope, and publish success test) still blocking full plan readiness.

## 2026-05-29T13:42:15.816+02:00 — GitHub Issue Spun Out: Plan 0020 README Work

**Event:** Christian requested README work spun out from plan 0020 into separate GitHub issue.

**Action Taken:**

- Verified no existing suitable README-scoped issue in open backlog
- Created GitHub issue #35: "Update README with project overview and installation guide"
- Issue labeled with 'mvp' (label already existed)
- Issue scope: Project-level README documentation separate from CLI-specific publishing artifacts

**Issue URL:** https://github.com/chA0s-Chris/ShadowDrop/issues/35

**Rationale:** Plan 0020's "README updated" criterion conflates CLI release artifacts documentation with project-level README. Spinning out the README work maintains clear scope boundaries and lets each work stream own its domain (CLI publishing vs. project documentation).

**Impact on Plan 0020:** The "README updated" acceptance criterion in plan 0020 can now be narrowed to CLI-specific release documentation (what binaries are available, how to download them) rather than the full project README scope. Recommend adjusting plan wording to "Update CLI release documentation" or delegating to issue #35 with link.


---

## 2026-05-29T13:51:32Z — Plan 0020 MVP Finalized: Scope Lock & Implementation Ready

**Event:** Plan 0020 scope finalized through three user directives applied to plan document.

**Directives Processed:**

1. **GitHub Workflow Addition:** Native AOT CLI build/validation GitHub Actions workflow now explicit in scope (not deferred).
2. **Fallback Removal:** Fallback-strategy decision removed from plan; AOT success for CLI already proven during development; no longer a meaningful open decision.
3. **README Scope Spin-Out:** README work extracted to GitHub issue #35 (project-level documentation separate from CLI publishing artifacts). Plan 0020 now focuses only on CLI binary distribution.

**Plan 0020 MVP Locked State:**

- **RID Matrix:** 6 explicit targets (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64)
- **Artifact Schema:** Flat layout `artifacts/cli/{version}/shadowdrop-cli-{version}-{rid}[.exe]` + `CHECKSUMS.sha256`
- **NUKE Integration:** Dedicated `Publish` target in scope
- **CI/CD:** GitHub Actions workflow with native + cross-compile strategy
- **Smoke Tests:** Explicit per-target strategy (direct test on accessible, build-only on cross-compiled)
- **Blocker Protocol:** Concrete; must document evidence in `.squad/decisions.md` before fallback considered
- **README:** Minimal CLI-specific release notes in plan; full project README delegated to #35

**Acceptance Criteria:** All nine criteria now concrete, testable, specific. No vague language. Implementer has exact definitions of success (file layout, naming, CI matrix, validation split).

**Status:** IMPLEMENTATION-READY. Await assignment to Sophie (CLI) or appropriate specialist.

**Cross-Team Impact:**

- **Tara (Platform):** No platform-specific work; all platform decisions locked in Plan 0019.
- **Sophie (CLI):** Can own NUKE Publish target + GitHub Actions workflow authoring without re-questioning scope.
- **Parker (Testing):** Smoke-test matrix and CI validation strategy now concrete; test design can proceed.

**Decision Records:** All three directives captured in `.squad/decisions.md`; plan document updated with finalized scope.

**Technical Debt:** None. Plan boundaries tight; no scope creep or ambiguous criteria remaining.

## 2026-05-29T14:29:36.720+02:00 — Issue #20 Scope Correction

**Summary:** Removed fallback/blocker-protocol language from issue #20 and plan 0020 per user directive. AOT viability is already proven for CLI; fallback strategy is out of scope for MVP.

**Changes:**

1. **Plan 0020:** Removed Acceptance Criterion 8 (blocker documentation), kept criteria 1–7 (implementation-ready).
2. **Issue #20:** Removed "Blocker Fallback Protocol" section, tightened acceptance criteria from 8 to 7 items, kept concrete scope.

**Rationale:** Native AOT for CLI already proven (Plan 0001, Parker's builds). Cleaner contracts enable faster assignment.

**Cross-Team Impact:**

- Sophie/CLI: Scope now purely implementation-focused
- Parker/Testing: Smoke-test matrix and CI strategy unchanged
- Architecture: No fallback decisions needed; MVP all-in on AOT

**Status:** Issue #20 now locked and ready for assignment. Scope complete: 6 RIDs, flat artifact contract, NUKE target, GitHub Actions matrix, smoke-test selectivity. No ambiguity remains.
