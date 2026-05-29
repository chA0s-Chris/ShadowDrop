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
