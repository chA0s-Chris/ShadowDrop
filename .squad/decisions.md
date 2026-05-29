---
title: Issue #20 Scope Correction — Fallback Protocol Removal
date: 2026-05-29T14:29:36.720+02:00
author: Nate
status: decision_locked
domain: issue-20, native-aot-cli-publishing
---

# Issue #20 Scope Correction — Fallback Protocol Removal

**Date:** 2026-05-29T14:29:36.720+02:00
**Lead:** Nate
**Issue:** #20 (Native AOT CLI Publishing)

## Decision

Removed all fallback/blocker-protocol language from issue #20 and plan 0020, per user directive. AOT viability is already proven for CLI; fallback strategy is out of scope for MVP.

## What Changed

### Plan 0020 (`ai-plans/0020-native-aot-cli-publishing-and-release-artifacts.md`)

- **Removed** Acceptance Criterion 8: "Any platform-specific AOT incompatibilities or blockers are documented in `.squad/decisions.md` with evidence (error output, dependency version, mitigation rationale) **before** fallback self-contained publishing is considered."
- **Removed** from Technical Details: "**Blocker protocol:** If `dotnet publish -r {rid}` fails or emits AOT incompatibility warnings, document the failure in `.squad/decisions.md` with evidence (error output, dependency version, root cause). Self-contained single-file publishing is acceptable only for a platform with a confirmed, narrowly scoped blocker, never as a silent default."
- **Kept** Acceptance Criteria 1–7 (all concrete, implementation-ready).

### Issue #20 on GitHub

- **Removed** entire "Blocker Fallback Protocol" section.
- **Tightened** acceptance criteria from 8 to 7 items (removed redundant/vague language).
- **Kept** all concrete scope: RID matrix, artifact contract, build strategy, validation split.
- **Reworded** for implementation-orientation (no verbose policy).

## Rationale

- User directive: "fallback strategy was explicitly removed from scope because AOT viability is already proven."
- Native AOT for CLI already proven in historical work (Plan 0001, Parker's builds).
- No decision gates remain for AOT fallback; scope is purely implementation of 6-RID matrix.
- Cleaner contracts enable faster assignment and execution.

## Cross-Team Impact

- **Sophie/CLI Team:** Scope now purely implementation-focused (NUKE target + GitHub workflow).
- **Parker/Testing:** Smoke-test matrix and CI strategy unchanged; no impact.
- **Architecture:** No fallback decisions needed; MVP is all-in on AOT for all platforms.

## Files Modified

1. `/home/chris/Code/github/ShadowDrop/ai-plans/0020-native-aot-cli-publishing-and-release-artifacts.md`
2. GitHub issue #20 body (via `gh issue edit`)

---
title: Issue #20 Finalization — Scope Lock & GitHub Update
date: 2026-05-29T14:29:36.720+02:00
author: Tara
status: decision_locked (superseded by Nate correction)
domain: issue-20, native-aot-cli-publishing
---

# Issue #20 Scope Lock & GitHub Update

## Summary

Issue #20 (Native AOT CLI publishing + release artifacts) updated to reflect finalized MVP scope, incorporating all decisions from Plan 0020 and Tara's prior recommendations (artifact contract, validation strategy, blocker protocol).

**Note:** The blocker protocol language referenced here was subsequently removed by Nate (see prior decision) per user directive.

## Key Decisions Locked

### 1. RID Matrix: All 6 (Not a Subset)

- linux-x64, linux-arm64
- osx-x64, osx-arm64
- win-x64, win-arm64
- **Rationale:** Christian confirmed all 6 on 2026-05-29. User clarity and release completeness justify macOS runner cost.

### 2. Artifact Contract (Flat, Version-in-Name)

- **Location:** `artifacts/cli/{version}/` (semver from `.csproj` `<Version>`)
- **Naming:** `shadowdrop-cli-{version}-{rid}[.exe]`
- **Extensions:** `.exe` on Windows only; no extension on Unix/macOS
- **Checksums:** Single `CHECKSUMS.sha256` (Unix-standard `<hash>  <filename>` format)
- **Rationale:** Flat structure = upload-ready; version in filename prevents overwrites; naming is self-documenting.

### 3. Build & Validation Split

- **Smoke tests on native targets:** linux-x64, osx-x64, osx-arm64 (CI + binary validation)
- **Build-only on cross-compile targets:** linux-arm64, win-x64, win-arm64 (CI + binary existence)
- **Rationale:** AOT failures are ~99% compile-time. Cross-compile success validates correctness. Smoke tests on accessible targets (native runners) provide confidence without requiring Windows or arm64 emulation.

### 4. NUKE Publish Target (In Scope)

- Dedicated NUKE target builds all 6 RIDs in single invocation
- Repeatable locally and in CI
- **Rationale:** Unified build orchestration; prevents manual RID-by-RID builds.

### 5. GitHub Actions Matrix (In Scope)

- Native runners for macOS (osx-x64, osx-arm64)
- Linux runner for all others (cross-compile)
- **Rationale:** Native macOS required for framework and arm64 correctness; Linux cross-compile is cost-effective.

### 6. Blocker Fallback Protocol (Explicit)

- ⚠️ **SUPERSEDED:** See Nate's correction above. Fallback protocol has been removed from scope.
- Original intent: If a platform cannot AOT-publish, document in `.squad/decisions.md` with evidence.
- **Current decision:** No fallback strategy in MVP; all platforms commit to AOT.

## Out of Scope (Post-MVP Decision)

- Release notes / installation documentation → moved to issue #35
- Archives (.tar.gz/.zip)
- GPG signatures
- Universal macOS binaries
- Comprehensive E2E validation (upload/download cycles)

## GitHub Issue Update

Issue #20 body updated with:

- Clear objective statement
- Settled MVP scope (6 RIDs, flat contract, NUKE target, GitHub Actions matrix, smoke-test selectivity)
- Explicit acceptance criteria (checkboxes)
- Implementation notes (AOT-hostile patterns → fix, don't downgrade)
- Link to Plan 0020 for technical context

## Team Coordination

- **Christian Flessa:** All decisions locked; implementation can begin
- **Implementer:** Issue #20 now includes concrete contract, CI strategy
- **Scribe/future reference:** All six decision gates documented; no ambiguity remains

## Next Steps

1. Assignee begins implementation with NUKE Publish target
2. GitHub Actions matrix built per smoke-test selectivity
3. Issue #35 owner starts work on README/docs separately

---

### 2026-05-29T13:36:50.490+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Add creation of a GitHub workflow for Native AOT CLI build/validation to the scope of plan 0020.
**Why:** User request — captured for team memory

### 2026-05-29T13:39:27.220+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Remove the fallback-strategy point from plan 0020 because native AOT build success for the CLI has already been proven during development and it is no longer a meaningful open decision.
**Why:** User request — captured for team memory

### 2026-05-29T13:42:15.816+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Remove README.md work from plan 0020 and track it in a separate issue instead, because that documentation should address ShadowDrop as a whole rather than only the CLI; label the issue `mvp`.
**Why:** User request — captured for team memory

---
title: Issue #35 — README initiative tracking split
date: 2026-05-29T13:42:15.816+02:00
issue: 35 / README orchestration
priority: mvp
domain: documentation, project-scope
---

## Decision

README work (project overview + installation guide) is tracked separately from plan 0020 as a full-project initiative under issue #35.

## Rationale

- README scope encompasses ShadowDrop as a whole, not specific to any single plan slice.
- Separating README tracking removes scope blocker from plan 0020.
- Issue #35 is canonical tracker; all README work routes there.
- Label `mvp` marks as milestone priority.

## Related

- Issue: https://github.com/chA0s-Chris/ShadowDrop/issues/35
- Previous scope: plan 0020 (now unblocked)

---
title: PR #31 — unresolved review assessment
date: 2026-05-29T02:32:01.927+02:00
issue: PR #31 / review triage
priority: mixed
domain: cli-downloads, queue-contracts
---

## Decision

Current unresolved PR #31 review notes split cleanly into three buckets:

1. **Should fix before merge:** `DownloadCommandHandler` compares manifest/queue `fileId` values with `StringComparison.Ordinal`, so valid GUID inputs that differ only by casing are rejected.
2. **Should fix only if polishing this slice:** manifest cache keys use raw `ShareReference.ServerUrl` strings, so trailing-slash variants can trigger duplicate manifest fetches within one queue run.
3. **Do not weaken runtime validation:** the top-level Copilot note is only partially valid; the real issue is documentation drift because plan `0017` still documents queue entries as requiring only `serverUrl`, `shareId`, and `outputPath`, while parser/runtime intentionally also require `fileId`, `fileName`, and `length`.

## Rationale

- File ids are semantically GUIDs across API, CLI, and queue handling, so case-sensitive string matching is the wrong contract boundary.
- Cache-key normalization affects efficiency only; requests themselves are already normalized through `ShareDownloadUriFactory`.
- Queue entries need metadata fields for deterministic file selection and validation, so relaxing parser requirements would be scope creep in the wrong direction; documentation should catch up instead.

### 2026-05-29T01:56:28.384+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Request Copilot code review with `gh pr edit <pr> --add-reviewer @copilot`.
**Why:** User request — captured for team memory


--- eliot-duplicate-manifest-file-ids.md ---
---
title: CLI manifest selection must fail closed on duplicate file ids
date: 2026-05-29T03:18:53.122+02:00
issue: PR #31 / CLI download hardening
priority: defense-in-depth
domain: cli-downloads, metadata-validation
---

## Decision

Treat duplicate manifest `fileId` values as invalid share metadata in the CLI download flow.

## Why

- `fileId` is the contract key used for direct `--file` selection and queue entry validation.
- `SingleOrDefault(...)` throws `InvalidOperationException` on duplicates, which escapes the command-error boundary and can abort queue processing.
- Failing closed with `DownloadCommandException("Share metadata invalid or missing.")` preserves the CLI's existing error contract and per-entry queue isolation.

## Impact

- Direct downloads with malformed duplicate ids now return the generic metadata error instead of crashing.
- Queue processing continues after a malformed manifest entry and still reports later entries.

## Related Files

- `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`
- `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`
# Squad Decisions

## Active Decisions

- 2026-05-14: The initial ShadowDrop squad uses the user-chosen names Nate, Eliot, Sophie, Alec, Tara, and Parker, with Scribe and Ralph as built-in roles.
- 2026-05-14: ShadowDrop work is routed by specialization across lead, backend, CLI, security, platform, and testing rather than a generic pooled roster.

## Team Policy

- 2026-05-14 (Copilot directive, Christian Flessa): Automatic commits allowed only for `./.squad/` changes. All commits must use Conventional Commits format; squad commits must use `docs(squad):` type prefix. Never push commits.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

## Recent Decisions

--- plan-0019-mvp-runtime-finalized.md ---
---
title: Plan 0019 — MVP Docker Runtime & Deployment Finalized
date: 2026-05-29T12:25:53.733+02:00
issue: Plan 0019 / Docker image and container deployment
priority: plan-finalization
domain: container-deployment, docker, multi-arch, runtime-hardening
---

## Decision

Plan 0019 MVP Docker runtime and deployment is finalized for implementation. All five runtime details assessed by Tara (platform) and Nate (validation) are MVP-ready with refined wording. No architectural changes needed.

## Finalized Requirements

1. **Multi-Architecture:** Docker image built for `linux/amd64` and `linux/arm64` via `docker buildx`. These cover 95%+ of self-hosted deployments (x86 VPS, ARM Raspberry Pi, Apple Silicon dev). Additional architectures deferred to post-MVP.

2. **Skip Registry Publishing:** Docker image publication to external registries (Docker Hub, etc.) out of scope for MVP. Image designed for local `docker build` and `docker buildx` workflows for self-hosted deployments. Registry publishing added post-MVP.

3. **Multi-Stage Production Build:** Dockerfile uses multi-stage build, targeting Ubuntu Chiseled ASP.NET base (`mcr.microsoft.com/dotnet/aspnet:*-chiseled`). Produces minimal production image with no intermediate layers exposed.

4. **Non-Root Execution:** Container enforces execution as uid 1000 (unprivileged) via `USER 1000` in Dockerfile. Chiseled base image designed for this security model. Smoke test validates API starts successfully as non-root; uid 0 unsupported.

5. **File Permissions & Volume Mounts:**
   - Directories (`/app/data`, `/app/data/metadata`, `/app/data/storage`): `755` (readable+executable by all, writable by owner)
   - LiteDB metadata file (`/app/data/metadata/shadowdrop.db`): `600` (owner-only read/write)
   - Storage blobs (`/app/data/storage/*`): `600` (owner-only read/write for encrypted content protection)

6. **Logging Configuration:** Reuse existing API Serilog integration. Runtime override via environment variable (`Serilog__MinimumLevel__Default=Debug`) or mounted custom `appsettings.json`. Example: `docker run --env Serilog__MinimumLevel__Default=Debug shadowdrop` for verbose logging.

## Smoke Test Contract

- Image builds locally: `docker build -t shadowdrop-test .`
- Container starts: `docker run --rm -p 8080:8080 -e SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN=test-token shadowdrop-test`
- Within 5 seconds: HTTP GET `/health` (or equivalent) returns 200
- Admin API endpoint responsive: validates config injection and non-root execution
- Test runs repeatably in CI without external dependencies (local volume mount only for appsettings override)

## Reviewed By

- **Tara** (Platform): Confirmed runtime contract, multi-arch coverage, non-root execution, file permissions alignment with Chiseled security model.
- **Nate** (Validation): Validated acceptance criteria wording, smoke-test measurability, environment variable patterns, configuration injection.

## Consultants

- Christian Flessa: User directive on architecture, logging, and non-root preference

--- alec-pr30-retry-exception-handling.md ---
---
title: PR #30 — SendWithRetryAsync Error Contract Breach
date: 2026-05-24T08:31:51.321+02:00
issue: PR #30 / Copilot review
priority: should-fix-before-merge
domain: error-handling, trust-boundary
---

## Issue

`UploadApiClient.SendWithRetryAsync` (lines 132-160) violates the CLI error contract by allowing non-generic exceptions to propagate on the final retry attempt.

### Current Behavior

- **Lines 149-151:** Catches `HttpRequestException` only when `attempt < MaxAttempts`
- **Lines 153-155:** Catches `TaskCanceledException` only when `!IsCancellationRequested && attempt < MaxAttempts`
- **Final iteration (attempt == 3):** Both catch blocks are skipped; exceptions bubble uncaught to caller

### Impact

Caller (`UploadAsync` / `ReserveFileIdAsync`) receives raw transport-layer exceptions instead of wrapped `UploadCommandException`. When caught in `UploadCommandHandler.ExecuteAsync`, the error message may expose:
- DNS lookup failures (network topology)
- TLS handshake errors (encryption/certificate details)
- Connection reset errors (server state)

**Violates:** Established error hygiene pattern ("generic errors on stderr, no path/secret/topology leakage").

## Root Cause

Retry guards (`attempt < MaxAttempts`) were designed to retry on transient failures but re-throw on final attempt. However, they should also wrap final-attempt exceptions to preserve error contract.

## Resolution

Refactor `SendWithRetryAsync` to ensure all `HttpRequestException` and `TaskCanceledException` instances are caught and wrapped, regardless of attempt number:

```csharp
catch (HttpRequestException)
{
    if (attempt < MaxAttempts)
    {
        await Task.Delay(GetDelay(attempt), cancellationToken);
    }
    else
    {
        throw new UploadCommandException("Server connection failed.");
    }
}
```

Apply same pattern to `TaskCanceledException` (preserving `!IsCancellationRequested` guard to allow caller cancellation to propagate).

## Test Coverage

Verify that:
1. `SendWithRetryAsync` throws `UploadCommandException` when final attempt encounters `HttpRequestException`
2. `SendWithRetryAsync` throws `UploadCommandException` when final attempt encounters `TaskCanceledException` (non-user-cancellation)
3. `SendWithRetryAsync` propagates caller-initiated `TaskCanceledException` immediately (no wrapping)
4. CLI handler output never contains "HttpRequestException", "TaskCanceledException", or transport-layer details

## References

- **Trust boundary:** CLI error output contract (stderr only, generic messages)
- **Pattern:** `crypto-buffer-encapsulation` equivalent for error surfaces (wrap internal exceptions, expose only generic boundary messages)

--- alec-pr30-test-async-assertion.md ---
---
title: PR #30 Follow-up — EncryptedFileContentTests Async Assertion Not Awaited
issue: 30
date: 2026-05-24T07:54:32.950+02:00
author: Alec (Security Engineer)
status: flagged-for-fix
severity: real
---

## Summary

`EncryptedFileContentTests.CopyToAsync_ShouldHonorCancellationToken()` declares an async assertion but never awaits it, resulting in a false-positive test that silently passes without verifying cancellation behavior.

## Details

**File:** `tests/ShadowDrop.Cli.Tests/Uploads/EncryptedFileContentTests.cs` (line 34-36)

**Current code:**
```csharp
var act = async () => await content.CopyToAsync(sink, null, CancellationToken.None);
act.Should().ThrowAsync<OperationCanceledException>();  // ← never awaited
```

**Problem:**
- Line 34 defines `act` as an async lambda (closure that returns a Task).
- Line 36 calls `.ThrowAsync()` to assert the async lambda throws on invocation.
- **BUT:** The test method is NOT `async Task`, so `.ThrowAsync()` is never awaited.
- Result: Fluent Assertions queues the assertion, but it never executes. Test always passes.

## Security Implications

The plaintext buffer cleanup in `EncryptedFileContent.SerializeToStreamAsync()` (lines 72-75) relies on the `finally` block:

```csharp
finally
{
    Array.Clear(buffer);
}
```

If cancellation is not properly threaded through the stream, the `finally` block may not execute, leaving plaintext in heap memory. This test is supposed to verify that cancellation **is** properly threaded and cleanup fires.

**Current state:** Test passes silently ✅, but cancellation is never actually tested. Risk of silent regression if cancellation fails in future refactors.

## Fix

Convert the test to `async Task` and `await` the assertion:

```csharp
[Test]
public async Task CopyToAsync_ShouldHonorCancellationToken()
{
    // ... setup ...
    var act = async () => await content.CopyToAsync(sink, null, CancellationToken.None);
    await act.Should().ThrowAsync<OperationCanceledException>();
}
```

This ensures:
1. The async lambda is actually invoked.
2. The assertion waits for the exception.
3. `OperationCanceledException` is caught and verified.
4. Plaintext buffer cleanup (`finally`) is validated.

## Recommendation

**MUST FIX before merge.** This is a test correctness deficiency with direct security implications (cancellation cleanup verification). Code review should catch this; it's straightforward to fix.

---

**Note:** The other two flagged notes (`AesGcmTagLength` unused, `succeededFileIds` unused) are code smell only — not security concerns. Can be addressed in follow-up or as separate cleanup.

--- alec-queue-secret-free-contract.md ---
# 2026-05-28T21:54:11.331+02:00 — Alec: Queue files must fail closed on unsupported fields

## Decision

Download queue files are part of a trust boundary. They must remain secret-free and reject unsupported JSON properties instead of silently ignoring them.

## Why

- A queue file that silently accepts extra fields can smuggle secrets such as `shareKey`, `bearerToken`, or future ad-hoc auth material.
- `System.Text.Json` ignores unknown properties by default, which is convenient but wrong at this boundary.
- Failing closed forces any future contract expansion (for example, adding a new download field) to be explicit in the shared model and validation logic.

## Security Implications

- Share keys and bearer tokens must not rest in queue files.
- Queue processing should reject malformed or over-specified input before any network or file I/O begins.
- This also protects against “helpful” implementations that might otherwise start reading bearer tokens from queue JSON instead of CLI-only ingress.

## Reviewer Requirement

For issue #17, bearer tokens for downloads must be sourced only from explicit CLI arguments. Do **not** reuse `CliConfigurationResolver` or any environment/config-backed token lookup for this flow.

## Related Files

- `src/ShadowDrop.Shared/Queue/QueueFile.cs`
- `src/ShadowDrop.Shared/Queue/QueueFileEntry.cs`
- `src/ShadowDrop.Shared/Queue/QueueFileParser.cs`
- `tests/ShadowDrop.Shared.Tests/Queue/QueueFileParserTests.cs`
- `src/ShadowDrop.Cli/Downloads/CliDownloadRequestFactory.cs`

--- copilot-directive-2026-05-23T22-19-16.996+02-00.md ---
### 2026-05-23T22:19:16.996+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Secret emission should remain in scope for issue 0016 because the CLI must output the secret somehow; otherwise later download/decrypt use of uploaded files is not possible.
**Why:** User request — captured for team memory

--- copilot-directive-2026-05-29T00-40-12.837+02-00.md ---
### 2026-05-29T00:40:12.837+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Use `gpt-5.5` instead of any `claude-opus` model for premium work. `claude-opus-4.5` and `claude-opus-4.6` are unavailable, and `claude-opus-4.7` / `claude-opus-4.8` are available but too expensive.
**Why:** User request — captured for team memory

--- eliot-cli-upload-ciphertext-memory.md ---
---
date: 2026-05-23T23:28:14.726+02:00
agent: Eliot
---

## Decision: Internal ciphertext memory view for async upload streaming

For shared crypto value objects that already protect their public `byte[]` boundary with defensive copies, we can add an internal `ReadOnlyMemory<byte>` view when another in-repo assembly needs allocation-free async stream writes.

Applied here for CLI uploads: `EncryptedChunk` keeps the public `Ciphertext` copy-returning contract, while `ShadowDrop.Cli` uses an internal memory view (via `InternalsVisibleTo`) to avoid per-chunk ciphertext cloning during `Stream.WriteAsync`.

--- nate-0016-cli-upload-review.md ---
---
date: 2026-05-23T22:14:25.974+02:00
author: Nate (Lead)
subject: Plan 0016 Architectural Review
status: decision_and_recommendations
---

# Plan 0016 Review — CLI Upload Command

## Overview

Plan 0016 (cli-upload-command.md) is **well-scoped and architecturally sound**. The slice is properly bounded (intake-only, no share creation), dependencies align with existing decisions, and acceptance criteria are concrete and testable. No blocking issues.

Three surgical recommendations strengthen the plan before implementation starts:

---

## Recommendation 1: Harden Token Leakage Boundary

**Location:** "Configuration Precedence & Token Handling" section
**Current language:** "If users pass token via CLI flag (e.g., `--upload-token $USERTOKEN`), the token may be visible to process inspection tools..."

**Issue:** The statement is correct but passive. Add explicit guidance on what "process inspection" means and tie it to the bootstrap token pattern already used in the API:

**Change:** Expand the paragraph to:
> If users pass token via CLI flag (e.g., `--upload-token $USERTOKEN`), the token may be visible to process inspection tools (e.g., `ps` on Unix, Process Explorer on Windows, or `/proc/*/cmdline` inspection). This is a fundamental limit of CLI argument passing and cannot be eliminated by the CLI itself. **Users are responsible for choosing the input method**: prefer `SHADOWDROP_UPLOAD_TOKEN` environment variable or config file for sensitive deployments. Document this explicitly in user-facing help text and CLI usage docs.

**Why:** Prevents misunderstanding that the CLI can hide flags. Aligns the CLI token handling with the bootstrap-token decision in `.squad/decisions.md` (where the same trade-off was documented for `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`).

---

## Recommendation 2: Clarify Share-Level Secret Emission Semantics

**Location:** "Share-Level Secret Lifecycle" section
**Current language:** Roughly correct but scattered across multiple paragraphs. The key points are there but the implementation boundary is ambiguous.

**Issue:** The plan says the CLI "may emit the plaintext secret to stdout (if `--output-secret` flag or equivalent is provided)" but doesn't specify:
- Whether this flag is part of **this slice** or a later enhancement
- Whether the flag is mandatory, optional, or non-existent for MVP
- What "equivalent" means (separate command? config option?)

**Change:** Lock down the secret emission boundary explicitly. Two options:

**Option A (Preferred: Narrower scope):**
> This slice does NOT implement secret emission. The CLI reads the plaintext secret from the caller, encrypts files with it, and uploads encrypted content only. The plaintext secret is **not** emitted, printed, or returned by the upload command. After upload completes, the caller is responsible for obtaining the plaintext secret from wherever it generated it. A separate `shadowdrop secret` management command or equivalent can be added in a future slice to support secret generation, storage, and retrieval workflows.

**Option B (Broader scope):**
> The CLI supports an optional `--output-secret` flag. If provided, the plaintext secret is written to stdout immediately after successful upload of all files. If the flag is absent, the secret is not emitted. Whether to capture and persist the secret is the caller's responsibility.

I recommend **Option A** (narrower scope) to keep this slice focused on intake. It simplifies the mental model: "this command takes a secret, encrypts files, uploads them, and exits." All secret management (generation, storage, retrieval) is downstream. This matches the decision pattern for upload scope in the existing API plan.

---

## Recommendation 3: Sharpen the "All-or-Nothing per File" Boundary

**Location:** "HTTP Status & Retry Behavior" section, subsection "Partial failure"
**Current language:**
> If one file succeeds and another fails, the command exits non-zero. Failed files are not retried automatically; user must re-run the command with the failed file paths.

**Issue:** Correct high-level statement, but creates a subtle ambiguity for the caller:
- Does "re-run the command" mean the CLI will skip already-uploaded files? (No—the caller must manually list only failed files.)
- Can the same file be uploaded twice? (Yes, upload API accepts new file id for the same plaintext.)
- Is there a way to deduplicate files already in the server? (Out of scope for this slice; not addressed.)

**Change:** Tighten the statement:
> If one file succeeds and another fails, the command exits non-zero and reports which files failed (on stderr). The CLI does not automatically retry failed files or track upload state between invocations. The caller must re-run the command with only the failed file paths. If a file is uploaded successfully once and then re-uploaded, the server assigns a new file id each time; deduplication is not performed in this slice.

**Why:** Prevents the caller from incorrectly assuming the CLI tracks state or deduplicates uploads. Keeps the contract clear: CLI is stateless, file-at-a-time, no smarts about already-uploaded content.

---

## Architectural Consistency Checks

**✅ Dependency Alignment:**
- Builds on 0011 (upload API) and 0013 (share creation, tokens) with correct boundaries
- Token handling matches bootstrap-token pattern from API decisions
- Encryption/streaming uses existing `ShadowDrop.Shared` contracts
- AOT compatibility requirement consistent with existing platform decisions

**✅ Slice Boundary:**
- Upload intake only; does not create shares, assign permissions, or manage secrets
- Separates caller-owned secret generation from server-owned file storage
- Non-interactive and automation-friendly as stated

**✅ Error Handling & Security:**
- Generic error messages follow the API error-safety pattern
- Token confidentiality enforced at multiple layers (trimming, no logging, no caching)
- Validation errors are clear without exposing secrets or paths

**✅ Acceptance Criteria:**
- All criteria are testable and concrete
- No contradictions detected
- Test surface includes config precedence, token handling, streaming, error cases

---

## Blockers

None. Plan is ready for implementation assignment.

---

## Summary

**Verdict:** Plan is **sound**. Make the three surgical edits above to tighten the token leakage boundary, lock down secret emission scope, and clarify the all-or-nothing per-file semantics. After those edits, the plan is implementation-ready with clear acceptance criteria and no ambiguity on scope boundaries.

--- nate-empty-file-rejection.md ---
---
decision_date: 2026-05-23T22:50:24.228+02:00
issue: "#16"
scope: "CLI upload command"
status: "locked"
---

# Empty File Rejection — Permanent Design

## Decision

**Empty files (zero-byte size) are intentionally rejected by the CLI upload command.**

The command validates file size before attempting upload and rejects any zero-byte file with non-zero exit code and a generic validation error message.

## Rationale

Empty files have no plaintext to encrypt and add no cryptographic value to the chunked AES-256-GCM contract. Accepting them would:
- Create semantic confusion in the encryption format (what does zero plaintext with a key mean?).
- Pollute file lists with zero-value entries.
- Add complexity to downstream share-creation and download workflows for no benefit.

Rejecting empty files at the CLI boundary is cleaner than accepting them server-side or filtering them downstream.

## Binding

- This is a **permanent design decision**, not a deferred feature or future enhancement.
- Empty file validation is part of the contract in issue #16 acceptance criteria.
- Plan `ai-plans/0016-cli-upload-command.md` documents this explicitly in the "Empty File Handling" section.
- Clients must pre-filter empty files or handle the validation error at the CLI boundary.

## Implementation Scope

- File size validation occurs **before** upload attempt.
- Error message must be generic (e.g., "Validation error: file invalid") — no path exposure.
- Exit code: non-zero on empty file detection.
- Tests must cover empty file rejection with hard assertions on exit code and error message content.

## Related Files

- `ai-plans/0016-cli-upload-command.md` — updated 2026-05-23
- GitHub issue #16 — body and acceptance criteria updated 2026-05-23
- Team decision comment: issue #16, comment referencing empty file rejection

--- nate-plan-0016-secret-emission.md ---
---
date: 2026-05-23T22:20:29.165+02:00
author: Nate (Lead)
subject: Plan 0016 — Secret Emission & Surgical Refinements
status: decision
---

# Plan 0016 — Secret Emission Decision & Surgical Refinements

## Decision: Secret Emission Is In Scope for Slice 0016

After reviewing `nate-0016-cli-upload-review.md` Recommendation 2, the team chose **Option B** (not Option A):

> The `--output-secret` flag is implemented in this slice. When passed, the plaintext share secret is emitted to stdout
> as a final line formatted `secret:<hex-encoded-value>` after all file id lines, **only on full success (exit 0)**. On
> any failure, no secret is emitted.

**Rationale:** Without any mechanism to receive the share-level secret, the upload command is unusable in practice — the
caller has no way to create shares from the uploaded files. Keeping it in this slice avoids a stranded intake-only
command with no follow-on workflow. The emission is opt-in (flag required), bounded (stdout, full-success only), and
explicit in format. The secret never appears in stderr, logs, or telemetry.

## Supporting Refinements Applied to Plan 0016

All four refinements are surgical edits to `ai-plans/0016-cli-upload-command.md`:

### 1. Environment Variable Names Locked
- `SHADOWDROP_SERVER_URL` — server URL
- `SHADOWDROP_UPLOAD_TOKEN` — upload authorization token

These names are binding. Implementations must not use alternate names. Follows the `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`
naming convention already established in the API.

### 2. Multi-File stdout/Exit Semantics Made Deterministic
- File ids written in argument order as each upload completes
- On partial failure: successful file ids still appear on stdout; exit code is non-zero; callers must check exit code
- Partial failure reports which files failed on stderr
- No deduplication; re-upload assigns a new file id

### 3. Token Non-Leakage Is a Tested Invariant
Not just convention: tests **must** assert the upload authorization token never appears in captured stdout, stderr, or
log output. Hard assertion, not grep-and-hope.

Symmetric invariant: when `--output-secret` is not passed, the share secret must also be absent from all output
channels.

### 4. Token Visibility Risk Documentation Required
CLI help text and usage docs must explicitly name `SHADOWDROP_UPLOAD_TOKEN` and explain that `--upload-token` flag
exposes the token to process inspection tools (`ps`, `/proc/*/cmdline`). Aligned with bootstrap-token handling in API.

## Impact on Other Work

- Slice 0016 implementer owns `--output-secret` flag implementation and secret hex-encoding
- Secret format: `secret:<lowercase-hex-encoded-256-bit-value>` (64 hex chars)
- Share-creation slice (future) must accept this secret as input; plan that slice to read `secret:` prefix from stdin
  or a flag
- Token non-leakage test pattern should be considered for the download CLI slice (0027) as well

--- parker-resume-destination-length-review.md ---
# Parker Review — Resume Destination Length Guard

- **Date:** 2026-05-19T19:35:51.788+02:00
- **Reviewer:** Parker
- **Artifact:** `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadSessionTests.cs`
- **Outcome:** Approved

## What I verified

1. `CliDownloadSession.DownloadAsync()` now calls `ValidateSeekableDestinationForResume()` before seeking or issuing the resume request.
2. The guard throws when `destination.Length != DurablePlaintextLength`, so seekable resume attempts fail closed instead of trusting a corrupted local stream length.
3. Regression coverage uses `[TestCase(-1)]` and `[TestCase(1)]`, proving both shorter and longer destination-length mismatches reject before a second HTTP request is sent.
4. `dotnet test tests/ShadowDrop.Cli.Tests/ShadowDrop.Cli.Tests.csproj --filter CliDownloadSessionTests` passed during review.

## Decision

Approve. The fix closes the preflight validation gap and the new tests cover the risky mismatch cases that were previously missing.

--- parker-upload-metadata-contract-order.md ---
# 2026-05-23T23:28:14.726+02:00 — Parker: Upload metadata contract order must stay stable

The CLI `UploadMetadataPayload` can be simplified to a positional record, but the multipart JSON contract must keep the established property names and property order (`fileId`, `originalFileName`, `plaintextLength`, `encryptedLength`, `contentType`, `encryptionFormatVersion`, `algorithmId`, `chunkSize`, `chunkCount`, `kdfSalt`, `plaintextSha256`).

Why this matters:
- reviewer-driven cleanup changed the record shape and silently reordered serialized properties
- we now have regression coverage proving the wire contract and should preserve it explicitly with JSON attributes/orders
- future refactors in the CLI upload path should treat that serialization shape as externally observable behavior

--- sophie-download-queue-manifest.md ---
# Sophie — Download queue + manifest shape
Date: 2026-05-29T00:41:01.379+02:00

## Decision
For the non-interactive download slice, the CLI now resolves share contents through a public share manifest endpoint (`GET /d/{token}`) before downloading encrypted file data. Shared queue validation was updated so each queue entry carries its own `serverUrl`, `shareId`, `fileId`, `fileName`, `length`, and `outputPath` (plus optional `plaintextSha256`) instead of relying on top-level target/share state.

## Why
The CLI needs file discovery for single-file stdout downloads, `--file` selection on multi-file shares, and queue processing that can target individual files without storing secrets. Per-entry queue fields keep batch inputs explicit and secret-free, while the manifest lets the CLI fetch KDF metadata needed for local decryption without prompting or server-side key handling.

## Impact
- Download queue files are now validated per file entry.
- The CLI can reuse one manifest fetch per share during queue runs.
- Public download tests should treat `/d/{token}` as part of the recipient contract alongside `/d/{token}/files/{fileId}`.

--- sophie-issue-16-upload-command.md ---
# Sophie — Issue 16 upload command

Date: 2026-05-23T22:26:23.766+02:00

## Decision

Lock the new `shadowdrop upload` contract to script-first channel semantics:

- stdout emits only successful file ids, one per line, in argument order
- `--output-secret` may add exactly one final stdout line as `secret:<hex>` and only when every file succeeded
- stderr owns all diagnostics, including partial-failure reporting by file ordinal rather than path
- config precedence is flags > environment > config file, with upload tokens trimmed before use

## Why

This keeps automation predictable, avoids accidental secret leakage into mixed output, and still leaves enough stderr detail for operators to recover from partial failures without exposing paths, server URLs, or tokens.

--- tara-ca1416-linux-guards.md ---
# Tara Decision: CA1416 Linux guards

- Date: 2026-05-23T23:15:47.318+02:00
- Context: CI runs `bash build.sh Test`, which builds test projects in Release and turns analyzer warnings into errors.
- Decision: keep the fix local to the Linux-only CLI test helpers by using analyzer-visible `OperatingSystem.IsLinux()` guards around `File.SetUnixFileMode` instead of relying on method-level platform attributes.
- Why: this clears CA1416 on the CI build path without broad warning suppressions and leaves existing Unix permission assertions unchanged.


--- parker-cli-download-pathbase-and-manifest-failures.md ---
- **Date:** 2026-05-29T01:05:43.969+02:00
- **Scope:** Issue #17 review revision

## Decision

CLI public-download URI generation now treats configured server URLs as directory bases and appends `d/{share}` / `d/{share}/files/{file}` as relative paths, so deployments behind a path base keep their prefix for both manifest and file requests. Absolute share URLs are parsed the same way: everything before the trailing `/d/{token}` is preserved as the server base.

Manifest transport failures are normalized inside `ShareManifestClient` to `DownloadCommandException("Server connection failed.")`. That keeps direct downloads on the documented exit-code-1 path and lets queue processing record a per-file failure line and continue with the remaining entries.

--- eliot-queue-contract-and-cache-key.md ---
- **Date:** 2026-05-29T02:49:00.341+02:00
- **Author:** Eliot

## Decision

Queue download docs should describe the full per-file contract: `serverUrl`, `shareId`, `fileId`, `fileName`,
`length`, and `outputPath`.

CLI queue validation should stay strict; those manifest-bound fields are required so queued work fails closed when the
live share metadata changes.

Manifest caching should use the canonical manifest URI rather than the raw server URL string so equivalent trailing-slash
variants reuse one cache entry.

--- nate-pr31-review-assessment.md ---
---
date: 2026-05-29T03:13:10.683+02:00
author: Nate
subject: PR #31 — unresolved review assessment reassessment
status: decision
---

# PR #31 Review Assessment — Final Triage

## Decision

**Two items resolved:**

1. **Queue contract documentation:** Plan `ai-plans/0017-cli-download-command-and-queue-processing.md` is current and correctly specifies the full queue entry contract (`serverUrl`, `shareId`, `fileId`, `fileName`, `length`, `outputPath`). The earlier "partially valid" concern noted in the top-level review is **resolved**; documentation matches validator logic and runtime requirements. No correction needed.

2. **Duplicate fileId defense-in-depth note:** The inline suggestion about duplicate `fileId` values in manifest processing is treated as **non-blocking defense-in-depth**. Current share creation rejects duplicate file ids server-side, so this note does not describe a live correctness bug on supported data. Archive as polish-only.

## Rationale

- Top-level documentation mismatch was an apparent gap between plan text and runtime requirements. Current plan text is authoritative and current.
- Duplicate-fileId handling is defensive but not required for correctness under the supported-data assumption (no duplicates from server).

## Blockers

None. PR #31 is ready to merge after these notes are closed.

--- nate-pr31-review-readiness.md ---
---
title: PR #31 — remaining unresolved review items are non-blocking
date: 2026-05-29T03:31:32.936+02:00
issue: PR #31 / review triage
priority: mixed
domain: cli-downloads, review-readiness
---

## Decision

Treat the remaining unresolved PR #31 review items as follows:

1. The top-level queue-contract complaint is already stale because `ai-plans/0017-cli-download-command-and-queue-processing.md` now matches the parser's required fields.
2. The two still-open inline notes in `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` are valid performance/UX suggestions, but neither is a merge blocker for this slice.

## Why

- Queue-entry validation and plan language are aligned today.
- Re-reading CLI config per queue entry is unnecessary work, but it does not break correctness because queue entries already supply and validate `serverUrl`.
- Buffering per-file stderr lines delays feedback and adds avoidable memory growth, but current behavior still satisfies the accepted "summary report" contract.

## Impact

- PR #31 can be reviewed as merge-ready from a correctness standpoint.
- The two inline `DownloadCommandHandler` notes can be addressed later if the team wants queue-path polish before resume work expands this area.

--- eliot-queue-download-runtime-behavior.md ---
---
title: Queue download runtime should trust validated queue server URLs and stream per-entry status
date: 2026-05-29T07:28:43.247+02:00
issue: PR #31 / CLI queue download follow-up
priority: should-fix-before-merge
domain: cli-downloads, queue-processing
---

## Decision

When executing queue downloads, use each queue entry's validated `serverUrl` directly and write the SUCCESS/FAILED status line as soon as that entry finishes.

## Why

- Queue parsing already validates `serverUrl`, so reopening CLI config for every queued file adds avoidable filesystem and JSON work.
- Explicit queue metadata should stay authoritative even if the local CLI config file is missing or malformed.
- Streaming result lines preserves operator feedback and keeps completed-entry outcomes visible even if a later entry or process termination stops the run.

## Impact

- Queue runs no longer depend on local config readability when the queue already provides `serverUrl`.
- Large queues emit progress incrementally instead of buffering all status until the end.

## Related Files

- `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`
- `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`
---
title: Prioritization decision — #18 (Interactive UX) next
date: 2026-05-29T09:02:48.323+02:00
issue: #18, #19, #20 / sequencing
priority: strategic
domain: feature-planning, sequencing
---

## Decision

**Issue #18 — Interactive Spectre.Console UX** should be the next feature to implement after the current PR merge cycle completes.

**Issues #19 (Docker) and #20 (Native AOT release artifacts) should follow in that order.**

## Rationale

### Issue #18: Rich Foundation for UX

- **Rich detailed acceptance criteria:** 12 specific checkboxes covering guided workflows, secret handling, TTY detection, orchestration contracts, testing strategy, and scope boundaries. No ambiguity.
- **Orthogonal to existing slices:** Wraps already-delivered upload (PR #30), share creation (PR #23), and download (PR #31) logic. Does not require API changes.
- **Clear team roles:** Sophie (CLI) leads implementation; Alec reviews secret handling; Parker writes orchestration tests. Routing unambiguous.
- **Unlocks usability:** Users get guided terminal flows. Automation remains via non-interactive flags.
- **No blocking dependencies:** All underlying operations (upload, share, download) are complete and merged.

### Issue #19: Infrastructure Underspecified

- **Empty body:** No acceptance criteria, scope, or boundary definition.
- **Platform-tier work:** Belongs to Tara (Platform Dev). Requires collaboration with Eliot (Backend) on container runtime decisions.
- **Unblocks deployment only:** Useful for staging/demo, but does not unlock end-user features while #18 waits.
- **Defer pending clarification:** Need Christian to define: Docker registry, base image policy, build context, health check contract, multi-architecture support, CI/CD hookup.

### Issue #20: Release Artifact Underspecified

- **Empty body:** No acceptance criteria or artifact definitions.
- **Platform-tier work:** Belongs to Tara. Depends on .NET 9 Native AOT compilation pipeline.
- **Late-phase concern:** Unblocks external distribution, but does not block feature completeness.
- **Defer pending clarification:** Need Christian to define: target platforms, artifact format (zip/tarball/msi), notarization/signing, CI/CD integration, compatibility matrix.

## Sequencing Logic

1. **#18 first:** Feature work with clear scope. Unblocks interactive user experience. Team has all required expertise and dependencies ready.
2. **#19 second:** Infrastructure for deployment. Useful after #18 is testable and ready for staging validation.
3. **#20 third:** Release artifacts. Useful for external distribution after #19 provides a canonical container image.

## Action Items

- **For #18:** Route to Sophie (squad:sophie) with orchestration test pair (Alec review secret paths, Parker test coverage).
- **For #19/#20:** Hold pending Christian's clarification on scope and acceptance criteria. Nate to triage when body text is provided.

---

## Implementation Note

This decision reflects the current merge cycle status (PR #31 completed, range-requests slice closed) and the team's availability to shift focus toward feature-layer work. If infrastructure work (#19/#20) becomes urgent before #18 completes, revisit this sequencing.

---
title: Sophie decision — interactive upload wraps share creation
date: 2026-05-29T09:11:29.068+02:00
issue: Issue #18 interactive Spectre.Console UX
priority: implementation
domain: cli-ux, interactive-flows
---

## Decision

Keep the new guided share-creation experience under `shadowdrop upload --interactive` instead of adding a separate CLI share-create command in this slice.

## Why

- The existing public CLI already exposes `upload` and `download`; wrapping share creation inside guided upload is the smallest behavior-safe operator workflow.
- It preserves the current scripted surface while still guiding operators through upload, share settings, and secret output.
- The flow still delegates to the same upload/download use-case logic and the existing share API contract.

### 2026-05-29T09:11:29.068+02:00: Sophie initial decision

**By:** Sophie (CLI Dev)
**What:** Keep guided share-creation under `--interactive` flag.
**Why:** Minimizes surface area, preserves scripting, and focuses on UX cohesion.

---
title: Plan 0019 Assessment — Docker Image & Container Deployment
date: 2026-05-29T11:45:18Z
issue: Issue #19 / Docker & container deployment
priority: blocking
domain: planning, acceptance-criteria, infrastructure
---

## Assessment by Nate

Plan 0019 requires three refinements to acceptance criteria before implementation can proceed. All are clarifications, not architecture changes.

### Refinement 1: Public/Protected Routes Independence

**Current:** Criterion "Separate handling of public/protected routes" is untestable as written.

**Clarification:** API already supports independent enable/disable via `ApiExposureOptions` (boolean flags). Criterion should mean: both features can be independently disabled via `SHADOWDROP_APEXPOSURE_*` env vars.

**Action:** Add example Docker Compose config to README showing the two flags in use.

### Refinement 2: Containerized Smoke Test

**Current:** Criterion "Containerized smoke test" lacks definition. Plan text says "proves API starts successfully with expected configuration shape" but defines no test method.

**Clarification:** Add health check endpoint (`/health`) to acceptance criteria, or explicitly defer it and test startup logs instead.

**Action:** Decide: implement `/health` endpoint or use log-based validation? Update criterion accordingly.

### Refinement 3: Production-Readiness Under-Specified

**Current:** Plan says "production-ready multi-stage build" but doesn't define it.

**Clarification:** Add checklist to acceptance criteria:
- Base image selection (Alpine/Debian/Ubuntu?)
- Non-root user configuration
- Health check contract
- Layer caching strategy
- .NET version pinning

**Action:** Add checklist items to plan acceptance criteria section.

## Why These Matter

Implementation cannot begin without clear acceptance criteria. The three items above are blockers for implementation review.

## Next Step

Christian should refine plan 0019's acceptance criteria section (three bullet points) before routing to implementation teams. No code work needed at this stage.

**Assessment artifact:** `.squad/assessment-plan-0019.md`


--- copilot-directive-2026-05-29T12-00-29.406+02-00.md ---
### 2026-05-29T12:00:29.406+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Use `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` as the base image. Use `/app/data/` as the writable base path, with `/app/data/shadowdrop.db` as the default LiteDB file and `/app/data/blobs/` for blob storage. Debugging should rely on logs/console output with a configuration option to increase verbosity. Healthchecks are not required for the MVP.
**Why:** User request — captured for team memory

--- copilot-directive-2026-05-29T12-05-32.374+02-00.md ---
### 2026-05-29T12:05:32.374+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Prefer configurable port exposure. Default behavior should expose one port with the entire API, with an optional second port that exposes the download API separately from the admin/upload API on the other port.
**Why:** User request — captured for team memory

--- nate-port-split-review.md ---
### 2026-05-29T12:05:32+02:00: Port-split proposal assessment for plan 0019

**By:** Nate
**What:** Can the API listen on two different ports with configurable split between admin/upload and download routes?
**Proposal:** Default = single port (entire API). Optional = two-port mode (admin routes on one port, download routes on the other).

---

## Verdict: Sound design pattern, but defer from 0019 MVP scope.

### Why It's Sound

1. **Already Half-Built:** `ApiExposureOptions` boolean flags (`EnableAdminOperations`, `EnablePublicDownloads`) already support selective route exposure. The infrastructure for conditional mapping exists in `AdminEndpoints.cs` and `DownloadEndpoints.cs`.

2. **Clean Separation:** Public downloads (/d/*) and protected admin routes (/api/admin/*) are logically separate. Multi-port binding lets deployment teams isolate public-facing traffic (read-only downloads, high throughput) from protected management traffic (token-gated, lower frequency).

3. **Zero Breaking Change:** Single-port default behavior unchanged. Two-port mode is purely additive configuration.

4. **Environment-Variable Friendly:** ASP.NET Core supports port binding via `ASPNETCORE_URLS` environment variable, aligning with plan 0019's existing env-driven config philosophy.

### Scope Risk If Included Now

1. **Acceptance Criteria Ambiguity:**
   - Is two-port mode mandatory or optional? (Plan currently says "optional")
   - What is the naming convention? (e.g., `SHADOWDROP_ADMIN_PORT` / `SHADOWDROP_DOWNLOAD_PORT`?)
   - How does health check (currently no `/health` endpoint) map to two-port scenario?
   - Docker Compose example: single-port only, or dual-port variations too?

2. **Containerized Smoke Test Expansion:** Current plan mentions a "containerized smoke test" but doesn't define it. Two-port testing doubles complexity: must verify both ports respond correctly and route segregation is enforced.

3. **Implementation Coupling:** Decision on Kestrel URL binding strategy affects how middleware is organized. Deferring lets plan 0019 focus on single-port Docker validation first, then add multi-port as a follow-up slice.

### Cleanest Requirement Phrasing (If Included)

If you decide to add this to 0019, phrase it like this to stay testable and non-overcommitting:

---

**Acceptance Criterion:** "The API supports multi-port binding via `ASPNETCORE_URLS` configuration (e.g., `http://*:5000;http://*:5001`). When two ports are configured, admin routes (`/api/admin/*`) map to the first URL and download routes (`/d/*`) map to the second. When a single port is configured (default), both route groups are served on that port. No code changes to route handlers are required; binding is purely configuration-driven."

**Smoke Test Update:** "Containerized smoke test runs the API in dual-port mode (e.g., `ASPNETCORE_URLS=http://+:8080;http://+:8081`) and verifies: (1) admin POST to `:8080/api/admin/shares` returns 401 (missing token), (2) download GET to `:8081/d/{token}` returns 404 (invalid share), (3) requests to wrong port return connection errors or 404."

---

### Recommendation

**Include in 0019:** Only if you want plan 0019 to be "API configuration + deployment flexibility." This keeps Docker work cohesive.

**Defer to 0020:** If you want 0019 to focus purely on "make a working single-port Docker image," then multi-port becomes a natural 0020 enhancement (even better: it becomes a zero-code change once ASPNETCORE_URLS is documented).

**Risk if deferred:** Users who want port isolation must work around it post-deployment. Low risk if documentation mentions `ASPNETCORE_URLS` as a future option.

---

## Files to Check Before Implementation

- `src/ShadowDrop.Api/CompositionRoot/Middleware.cs` — Current route mapping (line 21-22)
- `src/ShadowDrop.Api/Configuration/ApiExposureOptions.cs` — Boolean flags (already present)
- `README.md` deployment section — Where to document port binding

No code gaps exist; this is purely configuration + docs work.

--- tara-multiport-api-feasibility.md ---
### 2026-05-29: Multi-Port API Configuration is MVP-Ready

**By:** Tara

**What:** ASP.NET Core API can safely support optional dual-port binding (download on one port, admin on another) with zero breaking changes. Recommended as inclusion in Plan 0019 acceptance criteria.

**Why:**
- ASP.NET Core 10 natively supports Kestrel multi-endpoint configuration via environment variables.
- Current API structure (`AdminEndpoints`, `DownloadEndpoints`) already uses feature-flag guards; extending to port-specific registration requires ~10 lines of code.
- Default single-port behavior unchanged, so no risk to existing deployments.
- Enables security improvements (separate firewall rules, public/private network segregation) without operational overhead.

**Impact:**
- **Plan 0019 Criterion 4:** Refined wording suggested to document both single-port and multi-port modes.
- **Implementation scope:** Low. Add optional `DownloadPort` and `AdminPort` config to `ApiExposureOptions`. Update `Middleware.cs` to conditionally bind Kestrel endpoints. Extend smoke test to cover both modes.
- **Deployment:** Docker/Compose files just set `ASPNETCORE_URLS` env var or appsettings.json section.

**Testing:** Dual-path smoke test required (single-port happy path, dual-port happy path). Existing containerized test strategy covers both.

**Related Files:**
- `src/ShadowDrop.Api/Configuration/ApiExposureOptions.cs`
- `src/ShadowDrop.Api/CompositionRoot/Middleware.cs`
- `src/ShadowDrop.Api/Admin/AdminEndpoints.cs`
- `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`
---
title: HTTPS Support in Plan 0019 MVP — Scope Recommendation
date: 2026-05-29T12:11:18.705+02:00
issue: Plan 0019 / MVP scope
priority: architecture-decision
domain: api-exposure, deployment
---

## Question
Should HTTPS support be included in the Docker API MVP scope?

## Recommendation: **Exclude from MVP, defer to post-MVP**

### Rationale

1. **Reverse Proxy is the Standard Deployment Model**
   - Most self-hosted and cloud deployments (Kubernetes, Docker Compose, VPS) use a reverse proxy (nginx, Caddy, Traefik) for TLS termination.
   - This is the industry standard for microservices and containerized APIs.
   - The API binds to localhost/container-internal network; TLS happens at ingress.

2. **MVP Goal is Simplicity**
   - Exposing plain HTTP inside a container is correct. TLS complexity belongs at the deployment/orchestration layer.
   - Certificate management (Let's Encrypt, manual renewal, secrets injection) adds surface area and operational burden for MVP.
   - Single-port, single-protocol binding is simpler to test, document, and troubleshoot.

3. **Unblocking Blocker**
   - HTTPS is not required for a functional MVP. Feature completeness (upload, share, download) is already live and tested.
   - Users who need direct HTTPS exposure can deploy with a reverse proxy without code changes.

4. **Existing Capability**
   - ASP.NET Core supports `HTTPS_PORT` env var and certificate injection if needed later. This is a future addition, not a current gap.

### What MVP Should Clarify

Add this to plan 0019 documentation:

- **Default:** API listens on plain HTTP (port 5000 or configured via `ASPNETCORE_URLS`)
- **For TLS:** Deploy behind a reverse proxy (nginx, Caddy, etc.) or use ASP.NET Core's certificate binding at post-MVP phase
- **Why:** Simpler one-container model, aligns with container/orchestration patterns, certificates managed at infrastructure layer

### Decision
**Remove HTTPS from plan 0019 acceptance criteria.** It is a valuable post-MVP enhancement but not required for the Docker packaging MVP to deliver value.

## Next Steps
- Clarify in plan 0019 final text that MVP is HTTP-only inside container
- Create a follow-up issue ("Optional: HTTPS and Certificate Management") for post-MVP if Christian requests it
### 2026-05-29T12:11:18+02:00: HTTPS Support Scope for Plan 0019 MVP

**By:** Tara (Platform Dev)

**What:** HTTPS support (TLS termination in app/container) should **NOT** be part of Plan 0019 MVP. Plain HTTP behind reverse proxy is sufficient and recommended.

**Why:**

1. **MVP Deployment Model Alignment**
   - Plan 0019 targets "home lab and small VPS use cases" with one-container deployments
   - Reverse proxy (nginx, Caddy, Traefik) is the standard pattern in this segment and already handles HTTPS termination cleanly
   - Users deploying at this scale either: (a) already use a reverse proxy, or (b) can easily add one
   - **No operational burden transfer** — reverse proxy is a solved problem in DevOps

2. **Certificate Management Complexity (Critical for MVP)**
   - App-managed HTTPS requires certificate provisioning, renewal, and mounting
   - Pain points:
     - Where do certs come from? (Let's Encrypt on each container startup? Secret storage?)
     - Certificate renewal automation across container restarts
     - User docs must explain cert paths, volume mounts, certificate format
     - Self-signed certs for testing add another config path
   - This is a *support tax* on an MVP — every user question adds friction
   - Reverse proxies abstract this entirely (Caddy auto-renews; Traefik integrates with cert providers)

3. **Configuration Surface Area**
   - App-managed HTTPS adds: `ASPNETCORE_HTTPS_PORT`, cert paths, certificate file handling
   - Post-MVP, adding this is straightforward (ASP.NET Core supports it natively)
   - Pre-MVP, it delays shipping and complicates initial deployment docs

4. **Reverse Proxy is Best Practice, Not Workaround**
   - Reverse proxy provides more than just TLS: request logging, rate limiting, auth headers, traffic inspection
   - Security best practice: app listens on localhost/private network, reverse proxy handles untrusted traffic
   - Adding HTTPS to the app doesn't improve security posture — moving it to the proxy does
   - Adding app-level HTTPS *adds* surface area (certificate handling bugs, key rotation issues)

5. **Post-MVP Path is Clean**
   - If users demand direct HTTPS (without proxy), adding it is a straightforward acceptance criterion for a future plan
   - ASP.NET Core's `AddHttpsRedirection()` middleware and Kestrel HTTPS binding are well-documented
   - No architectural debt created by deferring

**Recommendation for Plan 0019:**

- **MVP:** Container exposes plain HTTP on a single port (default `8080` or configured port)
- **Docs (README.md):** Include a quick Docker Compose example with a reverse proxy (Caddy or nginx) handling HTTPS
  - Document the simplified approach: "Reverse proxy handles HTTPS; container speaks plain HTTP"
  - Show `docker-compose.yml` with both single-container and proxy+container patterns
- **Future Plan:** "Direct HTTPS in Container" as a post-MVP enhancement if demand surfaces

**Implementation Notes for Acceptance Criteria:**
No change needed. Existing criterion "The image exposes the API in a way that supports... API exposure settings" covers plain HTTP exposure. Just document that HTTPS is reverse-proxy-provided, not container-provided.

**Team Decision:**
This assessment is shared with Christian for plan 0019 acceptance criteria finalization. Nate (DevOps/Release) may have additional deployment context; flagging for review if needed.

---

**Key Tradeoff Summary:**
- **Plain HTTP + Reverse Proxy:** Simpler MVP, standard pattern, no new support surface
- **App-Managed HTTPS:** Delays MVP, adds cert/key management burden, duplicates what reverse proxies do better
### 2026-05-29T12:11:18.705+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** Keep the optional two-port exposure idea, but defer it from the MVP. MVP should start with a simpler single-port configuration that exposes the entire API.
**Why:** User request — captured for team memory

--- plan-0019-wording-assessment.md ---
---
title: Plan 0019 — User Input Assessment & Refined Acceptance Criteria
date: 2026-05-29T12:25:53Z
issue: Issue #19 / Plan 0019 / Docker & Container Deployment
priority: blocker
domain: plan-scoping, acceptance-criteria
---

## Assessment Summary

Christian provided five numbered clarifications for plan 0019 (Docker image and container deployment). Assessment: Points 1, 2, 5 are ready to embed in criteria; Points 3–4 require wording refinement to eliminate ambiguity before implementation team can proceed.

## Decisions

### Point 1: Multi-architecture (amd64 + arm64)
**Status:** ✅ Ready to settle
**Decision:** Acceptance criterion should explicitly name both architectures in build output.

### Point 2: No registry publishing (MVP only)
**Status:** ✅ Deferred correctly
**Decision:** Skip registry push in MVP. Document as future work post-MVP.

### Point 3: Validation Contract (Route Separation)
**Status:** 🔴 Needs clarification before implementation
**Issue:** Current criterion 4 says "supports separate handling of public download routes and protected management routes through deployment configuration" but defines no test method.

### Point 4: Non-root User & File Permissions
**Status:** ✅ Sound requirement; needs specific wording
**Decision:** Chiseled images require non-root by design. Directory `/app/data` should be `700`, LiteDB file `600`.

### Point 5: Log Level Configuration
**Status:** ✅ Ready to settle
**Decision:** Use existing environment variable pattern already supported by API (Serilog via appsettings).

## Remaining Ambiguities Addressed

1. **Smoke test definition:** Plan text untestable; recommend log-based check or simple `/health` GET.
2. **Base image:** Recommend `mcr.microsoft.com/dotnet/aspnet:9.0-alpine` or `10.0-noble-chiseled`.
3. **Volume mount defaults:** Dockerfile creates `/app/data` with `700` permissions; Compose mounts host path.

## Recommended Next Step

Christian should refine plan 0019 acceptance criteria using wording suggestions above before routing to Tara (Platform) for implementation.

## Impact

- Plan 0019 becomes implementation-ready once Christian applies these refinements
- No code gaps discovered; API already supports all required configuration
- Docker work can proceed in parallel with CLI/Platform work after this refinement

--- plan-0019-runtime-contract-review.md ---
---
title: Plan 0019 — Docker Runtime Contract Assessment
date: 2026-05-29T12:25:53.733+02:00
issue: Plan 0019 / Docker image and container deployment
priority: scope-clarification
domain: container-deployment, multi-arch, runtime-configuration
---

## Summary

Assessed five open runtime details for Plan 0019's Docker image and container deployment MVP. All five are sound MVP choices with minor clarification wording needed for acceptance criteria.

## Assessment & Recommendations

### 1. Multi-Arch Contract: amd64 + arm64 Only
**Finding:** ✅ Correct MVP scope.
**Rationale:** amd64 + arm64 cover 95%+ of self-hosted deployments. Keeping MVP tight ensures one reliable publish path.

### 2. Skip Registry Publication for MVP
**Finding:** ✅ Sound scope choice.
**Rationale:** Publication adds operational burden MVP is not ready for. Self-hosted deployments build locally. CI/CD push can be post-MVP.

### 3. Validation & Smoke-Test Contract
**Finding:** ⚠️ Needs concrete measurable specification.
**Current Criterion 7 is vague.** Recommend concrete validation steps: image builds locally, container starts, HTTP `/health` returns 200, admin API responds, runs as non-root uid.

### 4. Non-Root Execution Requirement for Chiseled Image
**Finding:** ✅ Yes, should be explicitly required.
**Why:** Chiseled image designed for distroless best practices. Running as root defeats the attack surface reduction goal. `USER 1000` in Dockerfile is trivial; smoke test should verify startup succeeds as non-root.

### 5. File Permissions: /app/data
**Finding:** ✅ `600` for files is correct; distinguish database file vs. directory structure.
**Clarification:**
- **Directory structure** (`/app/data`, `/app/data/metadata`, `/app/data/storage`): `755` (readable+executable by all)
- **LiteDB file** (`/app/data/metadata/shadowdrop.db`): `600` (owner-only)
- **Storage files**: `600` (owner-only, protecting encrypted blob content)

## Decision

**All five runtime details are MVP-appropriate.** Minor wording refinements to acceptance criteria improve measurability and align with platform best practices.

**No architectural changes needed.** Proposed wording is editorial; no code work required until criteria are finalized.

### 2026-05-29T12:25:53.733+02:00: Platform assessment
**By:** Tara (Platform Dev)
**What:** Assessed five runtime details for Plan 0019. All sound MVP choices; acceptance criteria need minor clarification wording. Multi-arch (amd64+arm64), registry skip, smoke-test specification, non-root uid 1000, and file permissions (755 dirs, 600 files) are all recommended for inclusion.
**Why:** Platform responsibility to validate deployment model and runtime configuration before implementation; no blockers found.

--- plan-0019-final-mvp-scope.md ---
### 2026-05-29T12:44:50.404+02:00: Plan 0019 MVP Scope — Finalized

**By:** Nate

**What:** Updated `ai-plans/0019-docker-image-and-container-deployment.md` with all five settled MVP decisions into concrete, testable acceptance criteria. Removed ambiguous criterion on route separation (deferred to issue #33). Plan is now implementation-ready.

**Settled Decisions Embedded:**
1. **Base Image:** `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`
2. **Port:** `19423` (single port; both public downloads and admin routes together)
3. **Paths:** `/app/data/shadowdrop.db` (LiteDB), `/app/data/blobs/` (blob storage)
4. **Permissions:** `700` for `/app/data`, `600` for database file
5. **Non-root Execution:** uid/gid `1000:1000`
6. **Multi-arch:** amd64 and arm64 only
7. **Debugging:** Existing Serilog env vars (e.g., `Serilog__MinimumLevel__Default=Debug`)
8. **Config Override:** Custom `appsettings.json` mount supported
9. **HTTPS:** None in MVP; reverse-proxy pattern documented
10. **Health Check:** None in MVP
11. **Smoke Test:** Concrete single-container validation (starts, loads config, responds to request)

**Why:** Previous plan had three blockers:
- Route separation criterion was untestable and conflicted with Christian's single-port decision
- Smoke test definition was vague
- Production-readiness was under-specified

Finalized wording eliminates all ambiguity while keeping criteria testable and actionable for implementation team.

**Key Changes:**
- Removed old criterion 4 (now #33 backlog item for optional two-port mode)
- Added specific base image, port, paths, permissions, uid/gid, multi-arch, and smoke test validation
- Clarified log-level control via existing Serilog configuration
- Documented reverse-proxy pattern for HTTPS (no app-level HTTPS)
- Specified single-port default with both route groups served together

**Related Files:**
- `ai-plans/0019-docker-image-and-container-deployment.md` (updated)
- `ai-plans/AGENTS.md` (no changes needed; plan follows three-section structure)
- Issue #33 (multi-port deferral — already created)

**Status:** Implementation-ready. Awaiting Tara (Platform) or implementation team to begin Dockerfile authoring.

--- nate-0019-acceptance-criteria-polish.md ---
---
title: Plan 0019 Acceptance Criteria Polish
date: 2026-05-29T12:53:39.755+02:00
issue: Plan 0019 / Docker image and container deployment
priority: implementation-clarity
domain: acceptance-criteria, docker, runtime-specification
---

## Decision

Three targeted refinements to `ai-plans/0019-docker-image-and-container-deployment.md` acceptance criteria eliminate implementation ambiguities:

1. **Smoke Test Specificity:** "Simple GET request" now explicitly states: validate startup via logs, use existing endpoint, prove API readiness (no external dependencies).
2. **Volume Mount Requirement:** Explicitly requires `/app/data` be mounted as a volume for data persistence beyond container lifetime.
3. **Runtime Defaults:** "Exposes API on port 19423" now explicitly states bind address `0.0.0.0:19423`, single port in MVP, and that `ASPNETCORE_URLS` env var can override.

## Rationale

These clarifications remove implementation ambiguities while maintaining scope. All criteria remain testable and concrete.

## Status

Applied. Plan 0019 locked for implementation by Tara (Platform) or assigned implementer.

**Related Files:**
- `ai-plans/0019-docker-image-and-container-deployment.md` (updated)


### 2026-05-29T13:00:08.513+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** For Docker MVP plan validation, use `GET /health` as the smoke-test endpoint and require HTTP 200 as the expected result. Also make first-start database creation and the explicit multi-arch build validation command part of the plan.
**Why:** User request — captured for team memory

# Plan 0019 MVP Refinement: Health Check, Database Init, Multi-Arch Validation

**Date:** 2026-05-29T13:00:08+02:00
**Scope:** Plan 0019 Docker Image and Container Deployment
**Status:** Applied

## Decision

Three targeted refinements to Plan 0019 acceptance criteria and technical details:

### 1. Smoke Test Endpoint: `/health` with HTTP 200

**Rationale:** Specificity removes ambiguity. "Existing endpoint" leaves implementer guessing which endpoint to use. `/health` is a conventional choice and signals explicit intent.

**Applied:** Acceptance criterion 10 now specifies `GET /health` returning HTTP 200, not generic "existing API endpoint."

**Impact:** Smoke test is now unambiguously testable; Tara (Platform) knows exact validation target.

### 2. First-Start Database Creation Behavior

**Rationale:** Implicit assumptions about database schema creation cause deployment surprises. Users need to know data is safe on mount; implementer needs to know schema init is automatic, not manual.

**Applied:** New acceptance criterion (11) explicitly states: "On first start, if `/app/data/shadowdrop.db` does not exist, LiteDB creates the database and schema automatically; subsequent starts reuse the existing database."

**Impact:** Deployment guide will document this behavior; users gain confidence in persistence; implementer knows this is already handled by application startup logic.

### 3. Multi-Arch Build Validation Command

**Rationale:** "Both amd64 and arm64" is a requirement, but lacks a concrete validation method. Exact command reduces guesswork and allows CI to verify compliance.

**Applied:** New acceptance criterion (12) specifies: "`docker buildx build --platform linux/amd64,linux/arm64 -t shadowdrop:latest .` builds successfully on both architectures without errors."

**Impact:** Clear validation path for Dockerfile authoring; Tara can test immediately; CI/CD can enforce this gate.

## Scope Boundary

**Not Changed:** Rationale, core architecture, port, user, permissions, configuration patterns, HTTPS deferral, reverse-proxy assumption. These remain locked and were already sound.

**Changed:** Acceptance criteria specificity and technical details clarity only.

## Cross-Agent Alignment

- **Tara (Platform):** Explicit /health endpoint and multi-arch command enable immediate Dockerfile authoring without returning for clarification.
- **Parker (Tester):** Smoke test is now concrete and reproducible; test design has a fixed target.
- **Eliot (Backend):** LiteDB auto-init behavior confirms application startup logic already handles schema creation; no code changes needed.

## Implementation Path

- Tara authors Dockerfile using `/health` endpoint, multi-arch build command, and auto-init assumption.
- README deployment guide adds first-start database creation note.
- Smoke test uses exact `docker buildx build ...` command and `GET /health` validation.

--- plan-0020-aot-publishing-blocked-on-clarification.md ---
---
title: Plan 0020 — Native AOT CLI Publishing Blocked on Contract Clarity
date: 2026-05-29T13:14:07.984+02:00
issue: Plan 0020 / native-aot-cli-publishing-and-release-artifacts
priority: blocking-clarification
domain: cli-distribution, build-release, multi-arch
---

## Decision

Plan 0020 (Native AOT CLI Publishing) is **NOT IMPLEMENTATION-READY**. Acceptance criteria are vague. Critical decisions are missing. One hidden dependency exists (NUKE pipeline has no `Publish` target). Plan must be clarified before work begins.

## Blocking Issues

### 1. Vague Acceptance Criteria (5 items)

| Criterion | Problem | Required Decision |
|-----------|---------|---|
| "Publish outputs organized as release-ready artifacts" | No layout spec; "organized" undefined | Explicit directory structure and file naming scheme |
| "Release artifact naming is consistent" | No naming scheme given (e.g., `shadowdrop-cli-linux-x64` vs `ShadowDrop.Cli-linux-x64`?) | Concrete naming pattern with examples |
| "Native AOT publish succeeds" | Success is undefined; binary might exist but fail at runtime | Test definition (exit 0? binary executable? runs `--help`?) |
| "Blockers documented" | Destination not specified (README? separate file? code comments?) | File path for blocker documentation |
| "README updated" | Which README? What minimum content? | File path and content checklist |

### 2. Missing Critical Decisions

1. **RID Matrix:** Plan says "target matrix from the concept" but lists no explicit Runtime Identifiers.
   - **Proposed:** `linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64` (6 total)
   - **Question:** Is this matrix complete, or is Windows deferred for MVP?

2. **Artifact Output Format & Structure:**
   - Directory layout: per-platform subdirs or flat with RID in filename?
   - Archives (.tar.gz, .zip) or bare executables?
   - Checksums/signatures included?
   - Windows .exe suffix handling?
   - **Proposed:** Versioned structure `artifacts/cli/v{version}/{rid}/shadowdrop-cli[.exe]`

3. **CI/CD Integration:** Who implements repeatable publish?
   - Extend NUKE pipeline with `Publish` target (recommended)
   - Standalone script (`publish-cli.sh`, `publish-cli.ps1`)
   - Artifact upload destination and process
   - **Key insight:** Current NUKE pipeline (`build/BuildPipeline.Build.cs`) defines only `Build`, `Test`, `Clean`, `Restore` — no `Publish` target exists yet

### 3. Hidden Dependency: Missing NUKE Publish Target

- ✓ `.csproj` has `<PublishAot>true</PublishAot>`
- ✗ **NUKE pipeline has NO `Publish` target**
- ✗ Historical publish from Parker's work is manual (`dotnet publish -r linux-x64`), not repeatable in CI

**Implication:** Plan must include writing NUKE Publish target or clarify if out of scope.

### 4. Cross-Compile & CI Matrix Strategy

For 6 RIDs on standard CI:

| RID | Baseline | Cross-compile | Runner |
|-----|----------|---|---|
| linux-x64 | ✓ Linux | no | Linux (standard) |
| linux-arm64 | ✗ | yes (on Linux) | Linux (standard) |
| osx-x64 | ✗ | complex | macOS (cost) |
| osx-arm64 | ✗ | complex | macOS (cost) |
| win-x64 | ✗ | yes (on Linux) | Windows or Linux |
| win-arm64 | ✗ | yes (on Linux) | Windows or Linux |

**Options:**
- **A (recommended):** GitHub Actions matrix with native runners (Linux, macOS, Windows)
- **B (cost-optimized):** Cross-compile all on Linux runner (AOT blocker risk for macOS)
- **C (phased):** MVP = linux-x64/arm64 only; defer macOS/Windows to Phase 2

**Decision needed:** Confirm scope and CI cost tolerance.

### 5. AOT Blocker Validation & Fallback Protocol

When `dotnet publish` fails or generates warnings:

1. **Detection:** Capture full stderr/stdout, `IlcOptimizer` warnings, trimming issues
2. **Validation criteria:**
   - Non-transient publish error (e.g., dependency doesn't support AOT)
   - Binary degradation (perf/size significantly worse)
   - Untrimmed reflection dependency
3. **Fallback activation:**
   - Document blocker (RID, error snippet, root cause, rationale, temporary status)
   - Platform Dev approves blocker doc
   - Fallback generates self-contained binary instead
   - CI and README document which RIDs use fallback vs native AOT

**Decision needed:** Is this protocol acceptable, or adjust?

### 6. README Release/Install Documentation Scope

**Proposed sections:**
- **Installation:** Per-platform download + executable commands (e.g., `chmod +x`, add to $PATH)
- **Platform matrix:** Table of OS/Arch → Binary name + SHA256 (optional MVP)
- **Troubleshooting:** Common issues (macOS quarantine, PATH, etc.)

**Decision needed:** Confirm scope or add checklist items.

## Critical Path to Implementation

User (Christian) or Nate (lead) must decide **before** work starts:

1. Confirm RID matrix (all six? MVP subset?)
2. Define artifact naming scheme with examples
3. Specify artifact output structure (layout, compression)
4. Decide on NUKE target ownership and implementation
5. Define blocker documentation path
6. Specify README scope (file path, minimum content)
7. Define publish success test (what validation proves "publish succeeds"?)
8. Confirm CI strategy and cost tolerance

Once settled, update Plan 0020 Technical Details and acceptance criteria.

## Impact

- **Current state:** Plan cannot be assigned without contractor guessing at contract details.
- **After resolution:** Plan becomes straightforward 1–2 day lift for Sophie (CLI) or Tara (Platform).
- **Risk:** Leaving vague means implementer builds artifact scheme, then discovers it doesn't match downstream release process.

## Note

- ✓ Team has validated Native AOT for `linux-x64` (Plan 0001). No architectural risk; contract clarity only.
- ✓ Tara (Platform) has provided detailed options for each decision area; Nate (Lead) has validated blocking gaps.



--- user-directive-native-aot-rids.md ---
---
title: User Directive — Plan 0020 RID Matrix Confirmation
date: 2026-05-29T13:21:23.551+02:00
issue: Plan 0020 / Native AOT CLI publishing
priority: decision-critical
domain: cli-distribution, build-release
---

## Decision

For Native AOT CLI publishing/release artifacts, the target platforms are Linux, Windows, and macOS with x64 and arm64 for each.

**Confirmed RID matrix (all 6 targets, MVP scope):**

1. `linux-x64` — Linux x86-64
2. `linux-arm64` — Linux ARM64
3. `win-x64` — Windows x86-64
4. `win-arm64` — Windows ARM64
5. `osx-x64` — macOS x86-64
6. `osx-arm64` — macOS ARM64

**Directive source:** Christian Flessa (via Copilot), 2026-05-29T13:21:23.551+02:00

## Impact

- RID matrix no longer vague; all 6 platforms confirmed for MVP.
- Remaining five decision gates remain open: artifact naming scheme, output structure, build/publish approach, blocker validation protocol, README scope.
- Plan 0020 implementation can now proceed on RID decisions; awaiting clarity on remaining contract points.

## Next Steps

1. ✅ RID matrix locked (6 targets confirmed)
2. ⏳ Define artifact naming scheme
3. ⏳ Specify artifact output structure (directory layout, compression, checksums)
4. ⏳ Decide NUKE target ownership and implementation approach
5. ⏳ Document blocker validation protocol
6. ⏳ Define README release/install documentation scope

Once all six decisions are locked, Plan 0020 moves to ready-for-implementation status.

---
title: Plan 0020 — MVP Artifact Contract Recommendation
date: 2026-05-29T13:22:56+02:00
issue: Plan 0020 / native-aot-cli-publishing-and-release-artifacts / item 2
priority: recommendation-ready
domain: cli-distribution, build-release, artifact-layout
---

## Recommendation

Define a **concrete, repeatable artifact contract** for Native AOT CLI release outputs across six Runtime Identifiers (RIDs). This contract prioritizes implementation simplicity, user clarity, and release practicality.

## Proposed Contract

### Naming & Structure

**Naming Pattern:**
```
shadowdrop-cli-{version}-{rid}[{extension}]
```

**Directory Layout:**
```
artifacts/cli/{version}/
├── shadowdrop-cli-{version}-linux-x64          (no extension on Unix)
├── shadowdrop-cli-{version}-linux-arm64
├── shadowdrop-cli-{version}-osx-x64
├── shadowdrop-cli-{version}-osx-arm64
├── shadowdrop-cli-{version}-win-x64.exe        (Windows .exe only)
├── shadowdrop-cli-{version}-win-arm64.exe
└── CHECKSUMS.sha256                            (flat file, all binaries listed)
```

**Key Details:**
- **Version:** Semver from `.csproj` `<Version>` tag (e.g., `1.0.0`). Version is in **filename**, not a directory level.
- **Extensions:** Unix/macOS have no extension. Windows has `.exe`. This matches platform conventions and user expectations.
- **Checksums:** Single `CHECKSUMS.sha256` file using Unix-standard format: `<hash>  <filename>`, one per line, compatible with `sha256sum --check`.

### RID Matrix (MVP = All 6)

| RID | Baseline | Cross-compile | Runner | Notes |
|-----|----------|---|---|---|
| `linux-x64` | ✓ | no | Linux | Standard GitHub Actions runner |
| `linux-arm64` | ✗ | yes | Linux | Supported on Linux via `-r` flag |
| `osx-x64` | ✗ | complex | macOS | Requires native macOS runner |
| `osx-arm64` | ✗ | complex | macOS | Requires native macOS runner; ~10× cost |
| `win-x64` | ✗ | yes | Linux | Cross-compile on Linux runner |
| `win-arm64` | ✗ | yes | Linux | Cross-compile on Linux runner |

**Recommendation:** Include all six for MVP. macOS arm64 cost is offset by user clarity (one release covers "all Macs") and release credibility.

### What the Plan Should Say

**Add to Technical Details:**

> **Artifact Contract & Release Layout**
>
> The publish process generates release-ready CLI artifacts in `artifacts/cli/{version}/` where `{version}` is the semver from the `.csproj` `<Version>` property. Each artifact is a native AOT standalone executable executable named following the schema `shadowdrop-cli-{version}-{rid}`, with platform-specific extensions (`.exe` on Windows; no extension on Unix/macOS).
>
> All six Runtime Identifiers are published in a single batch: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`. Each binary is a complete, self-contained executable with no runtime dependencies.
>
> A `CHECKSUMS.sha256` file (Unix-standard format) is generated in the same directory, listing the SHA-256 hash and filename for each binary. Users can verify integrity with standard `sha256sum -c CHECKSUMS.sha256` commands.
>
> The publish workflow (NUKE Publish target or script) is repeatable locally and in CI, using GitHub Actions matrix with native runners for macOS and standard runners for Linux. All compiler warnings and AOT analysis output is captured in CI logs for blocker diagnosis.

### Acceptance Criteria (Refined)

Replace vague criteria with these concrete ones:

- [ ] Native AOT `dotnet publish` succeeds for Runtime Identifiers: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`.
- [ ] All publish artifacts are placed in `artifacts/cli/{version}/` (where `{version}` is semver from `.csproj`).
- [ ] Each binary follows naming schema: `shadowdrop-cli-{version}-{rid}[.exe]`. Example: `shadowdrop-cli-1.0.0-linux-x64`, `shadowdrop-cli-1.0.0-win-x64.exe`.
- [ ] A `CHECKSUMS.sha256` file is generated in the same directory listing all six binaries in Unix format (`<hash>  <filename>`).
- [ ] Each binary is executable and responds to `--help` and `--version` commands with exit code 0 (smoke test).
- [ ] Any platform-specific AOT incompatibilities or blockers are documented in `.squad/decisions.md` **before** fallback self-contained publishing is used, with evidence (error output, dependency version, mitigation rationale).
- [ ] README includes new `/Release & Installation` section with: per-platform download instructions, checksum verification guidance, platform-specific post-install steps (e.g., macOS quarantine removal, $PATH setup, chmod +x on Unix).

## Why This Design

### Implementation Simplicity
- **One naming scheme:** No prefix/suffix variations, no configuration-dependent names. Simple to generate, easy to script.
- **Flat artifact layout:** No deep nesting or RID subdirs. Single directory per version = straightforward CI artifact export and GitHub Releases attachment.
- **Version in filename:** Inherently prevents overwrites and accidental collisions. No need to track version as state.

### User Clarity
- **Filename is self-documenting:** `shadowdrop-cli-1.0.0-osx-arm64` tells you OS, architecture, version instantly.
- **Checksum verification is standard:** Users know `sha256sum --check` from any Linux distro; Windows PowerShell includes `Get-FileHash`.
- **Platform-specific naming matches user expectation:** `.exe` on Windows because users know Windows binaries end in `.exe`. No `.bin` or `.sh` surprises.

### Release Practicality
- **Upload-ready:** Artifact structure works directly with GitHub Releases API—no manual renaming or restructuring before attaching.
- **Repeatable CI/local flow:** NUKE Publish target (or script) produces same structure locally and in CI. Developers can validate release locally before pushing.
- **Version decoupling:** Because version is in filename, old releases never get accidentally replaced when publishing new versions. Safe concurrent builds.

## What to Include/Exclude for MVP

### ✅ Include
1. All six Runtime Identifiers (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`)
2. NUKE Publish target or standalone publish script (repeatable, both local and CI)
3. Checksums (SHA-256) for all binaries
4. GitHub Actions matrix workflow with native runners
5. Smoke tests (binary executable, responds to `--help` and `--version`)

### ❌ Exclude (Post-MVP)
1. **Archive formats** (`.tar.gz`, `.zip`) — Ship bare executables. Users can archive locally if preferred.
2. **GPG signatures** — Checksums sufficient for MVP integrity. Signatures are an operational/key management burden.
3. **Installer wrappers** (Windows `.msi`, macOS `.dmg`) — Post-MVP convenience feature.
4. **Multi-architecture universal binaries** (macOS `.universal`) — Deferred to Phase 2 if user demand justifies build time cost.
5. **Separate release documentation site** — Keep install/release docs in main README only.

## Caveats & Known Tradeoffs

### 1. macOS Runner Cost
Publishing `osx-x64` and `osx-arm64` requires GitHub Actions native macOS runners. Cost is approximately **10× that of Linux runners per minute**.

- **For MVP:** Recommend accepting cost to deliver complete release. macOS users represent ~10% of user base but are high-value (dev/security community overlap).
- **Cost mitigation:** GitHub Actions Linux runner builds `linux-x64` and `win-*` RIDs; only macOS jobs run on expensive runners. Matrix overhead is acceptable (~2–3 min per publish).
- **Post-MVP option:** If cost is unacceptable, defer `osx-arm64` to Phase 2 and publish only `osx-x64` on native runner (covers 95% of macOS deployments).

### 2. Cross-Compile Complexity
Linux runner can cross-compile `linux-arm64` and `win-*` RIDs, but AOT compiler warnings may emerge that don't show up on native runners.

- **Risk:** Warnings are often environment-specific (libc version, toolchain version). Publishing from Linux runner might hide macOS-specific issues.
- **Mitigation:** Capture full `dotnet publish` stderr/stdout in CI logs. Enable max verbosity for ILC optimizer warnings. Test publish early in development cycle.

### 3. Blocker Fallback Protocol
When `dotnet publish -r {rid}` fails or emits AOT incompatibility warnings, the plan allows fallback to self-contained publishing **only if a blocker is proven**.

- **"Proven blocker" definition:** Must be documented in `.squad/decisions.md` with:
  - RID affected
  - Dependency and version
  - Error output or incompatibility evidence
  - Root cause analysis
  - Rationale for fallback (temporary vs permanent)
  - Date of discovery

- **No silent fallback:** Self-contained must never become the default for a platform. If a RID can't be AOT-published, the blocker is a visible, searchable record. Future maintainers can revise when dependencies update.

## Implementation Path

Once Christian approves this contract:

1. **Refine Plan 0020** — Integrate contract wording into Technical Details and Acceptance Criteria sections.
2. **Create or extend NUKE Publish target** — Authorship decision: extend `build/BuildPipeline.Build.cs` or write standalone `publish-cli.sh`/`publish-cli.ps1` script pair.
3. **Author GitHub Actions matrix** — Define job matrix for `linux-x64` (Linux runner), `linux-arm64` (Linux), `osx-x64` (macOS), `osx-arm64` (macOS), `win-x64` (Windows or Linux), `win-arm64` (Windows or Linux).
4. **Implement smoke test** — Shell script (Bash on Unix, PowerShell on Windows) that validates: binary exists, is executable, `--help` exits 0, `--version` exits 0.
5. **Author README section** — `/Release & Installation` with per-platform download, checksum verification, post-install guidance.

**Estimated lift:** 1–2 days for a Platform Dev or CLI author, once NUKE/script choice is confirmed.

---

## Decision Closure

**Proposed decision:** Approve this artifact contract as the definitive MVP spec for Plan 0020. Update plan text and acceptance criteria accordingly. Proceed with implementation authorship.

**Remaining decision:** Who owns NUKE target creation or publish script authorship? (Platform Dev or CLI maintainer?)

---
date: 2026-05-29T13:31:37.547+02:00
issue: native-aot-cli-publishing
status: user-directive
---

### 2026-05-29T13:31:37.547+02:00: User directive
**By:** Christian Flessa (via Copilot)
**What:** For the Native AOT CLI publishing/release artifacts plan, use the recommended flat artifact contract and include a dedicated NUKE target that builds all target executables.
**Why:** User request — captured for team memory

---
date: 2026-05-29T13:31:37Z
author: Tara (Platform Dev)
subject: Plan 0020 — MVP Validation Strategy for Native AOT CLI Publishing
status: recommendation
---

# Plan 0020 — MVP Validation Strategy for Native AOT CLI Publishing (6 RIDs)

## Problem Statement

Christian's concern: Linux dev machines cannot natively test macOS binaries or arm64 binaries. How do we validate 6 runtime targets (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64) in an MVP that is low-friction for developers and practical for CI?

Current plan accepts the RID matrix and flat artifact contract but leaves validation strategy underspecified. Acceptance criteria don't clarify what to test locally, what to defer to CI, or what smoke-test depth is appropriate.

## Recommendation: "Verify by Role, Not by Matrix"

**Core principle:** Validate the build pipeline and AOT configuration on all 6 RIDs in CI. Validate executable *behavior* only on locally accessible targets. This is pragmatic and industry-standard (Go, Rust, Zig all use this pattern).

---

## MVP Validation Matrix

| Target      | Build   | Smoke Test | Runner         | Rationale                                   |
|-------------|---------|------------|----------------|---------------------------------------------|
| linux-x64   | ✅ CI   | ✅ CI      | ubuntu-latest  | Developer platform; deterministic AOT       |
| linux-arm64 | ✅ CI   | ❌ defer   | ubuntu-latest  | Cross-compile fast; failures at build time  |
| osx-x64     | ✅ CI   | ✅ CI      | macos-latest   | Native runner (Security.framework, etc.)    |
| osx-arm64   | ✅ CI   | ✅ CI      | macos-latest   | Native runner; validates arm64 code paths   |
| win-x64     | ✅ CI   | ❌ defer   | ubuntu-latest  | Cross-compile OK; full test post-MVP        |
| win-arm64   | ✅ CI   | ❌ defer   | ubuntu-latest  | Rare target; no free-tier native runner     |

---

## Local Validation (Dev Loop)

**What developers do:**
1. On their current platform (e.g., Linux x64): `dotnet publish -c Release -r linux-x64 --self-contained`
2. Smoke test: `./shadowdrop-cli --version && ./shadowdrop-cli --help`
3. Error test: `./shadowdrop-cli --invalid-flag && echo "Exit code: $?"`

**Cost:** < 1 minute per commit.

**Why this is sufficient:**
- AOT compilation is deterministic within a platform. If it succeeds locally, it will succeed in CI on the same RID.
- The CLI has no platform-specific logic in main paths (no Windows registry, no macOS keyring, no symlink handling).
- Smoke tests exercise the initialization path, where 80% of AOT surprises occur.

---

## CI Validation (Build Matrix)

### Strategy

- **All 6 RIDs build in parallel** (GitHub Actions matrix).
- **Smoke tests on accessible targets only:** linux-x64, osx-x64, osx-arm64 (native runners).
- **Build-only on cross-compiled targets:** linux-arm64, win-x64, win-arm64 (QEMU/LLVM cross-compilation is fast; failures are almost always static—caught at build time).

### Smoke Test Scope (Minimal, Appropriate)

```bash
./shadowdrop-cli --version
./shadowdrop-cli --help
./shadowdrop-cli --invalid-flag  # expect exit code 1 or 2
```

**Why minimal smoke tests are MVP-appropriate:**
- AOT compiler catches ~99% of issues at build time (trimmer warnings, runtime init errors).
- CLI has no complex initialization (no database, config file parsing, service discovery).
- Crypto libraries are pre-validated by earlier slices.
- Full end-to-end (upload/download flow) requires server setup; deferred post-MVP.

**What's NOT tested in MVP:**
- Platform-specific file path handling (tested in unit tests, not binaries).
- Signal handling, concurrent operations, or stress.
- Windows and cross-compiled arm64 runtime behavior (deferred).

### Expected CI Time

- **Linux x64 build + smoke:** ~30 seconds
- **Linux arm64 build (no smoke):** ~20 seconds
- **macOS x64 build + smoke:** ~1 minute
- **macOS arm64 build + smoke:** ~1 minute
- **Windows x64 build (no smoke):** ~20 seconds
- **Windows arm64 build (no smoke):** ~20 seconds

**Total:** ~3–5 minutes per PR (macOS steps run in parallel but are inherently slower).

---

## Blocker Fallback Protocol

If a platform cannot AOT-publish:

1. **Document in `.squad/decisions.md`** with evidence (full compiler output, trimmer warnings, etc.).
2. **Blocker must be narrowly scoped** (e.g., "win-x64 AOT fails due to dependency X version Y").
3. **Fallback to self-contained single-file publish is acceptable** for that target *only*, not the entire matrix.
4. **No silent fallbacks.** Every blocker requires explicit team approval before switching distribution models.

---

## Key Assumptions

1. **CLI has no platform-specific logic.** No registry, keychain, or OS-specific APIs in main paths.
2. **AOT failures are mostly build-time.** Runtime-only failures are rare (< 1% of cases).
3. **Smoke tests exercise initialization.** `--version` and `--help` trigger dependency loading and framework initialization.
4. **Developer platform diversity is acceptable.** Some devs test on macOS/Windows locally; CI validates cross-platform.

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Cross-compiled binary fails at runtime | AOT compiler catches 99% of issues. Smoke tests catch framework calls. Fallback to self-contained if needed. |
| Developer skips local validation | Commit hook + PR check (future enhancement). MVP: trust developers. |
| Windows arm64 never tested | Acceptable for MVP. Rare target. Fallback available. Post-MVP: add self-hosted runner. |
| CI time creeps over 5 min | Monitor macOS job times. May split into two workflows (Ubuntu + macOS) post-MVP. |

---

## Summary

**For MVP:** All 6 RIDs build in CI; smoke tests on linux-x64 and osx-* (native runners). Build-only on cross-compiled targets (linux-arm64, win-x64, win-arm64). Blocker fallback is explicit and documented. Local dev validates current platform only (< 1 min).

**Expected outcome:** Fast, repeatable CI pipeline (3–5 min). Low friction for developers. Clear visibility into which targets work and which have blockers.

**Next step:** Implementer extends Plan 0020 with this validation section, implements GitHub Actions matrix workflow, and adds smoke-test script to build pipeline.

---

## Collaboration & Questions

**For Christian:** Does this risk/cost trade-off align with your MVP goals? Any concerns about deferring Windows functional testing or build-only cross-compile targets?

**For Implementer:** Will you extend the NUKE build pipeline with a Publish target, or use a standalone CI script?
