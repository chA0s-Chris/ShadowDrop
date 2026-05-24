# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Security Engineer for ShadowDrop.
- The concept defaults to separate-key CLI decrypt mode and makes direct HTTP decryption an explicit opt-in.
- 2026-05-14T23:49:15.783+02:00 — Revised the shared crypto surface after PR #6 review. `FileEncryptionContext` now keeps the share-level KDF salt encapsulated via defensive copies, `Generate()` was replaced with `GenerateKdfSalt()` to avoid per-file salt generation, and `ChunkEncryptionService.DeriveContentKey()` now zeroes intermediate key material after constructing `ContentKey`. Validation coverage lives in `tests/ShadowDrop.Shared.Tests/Crypto/FileEncryptionContextTests.cs` and the broader shared test project.

- 2026-05-15T00:12:35.582+02:00 — Assessed PR #6 Copilot note on `EncryptedChunk.Ciphertext` mutability. Verdict: **should-fix**. The getter exposes the stored mutable `Byte[]` directly, violating the immutability contract of `sealed record`. Ciphertext is not key material, so mutation causes `CryptographicException` on decrypt (detectable), not silent secret leakage — this is a correctness/contract concern rather than a secret-exposure concern. However, it is directly inconsistent with the `FileEncryptionContext` pattern already applied (defensive-copy getter + internal span), violates the `crypto-buffer-encapsulation` skill anti-pattern, and leaves caching/retry layers open to subtle corruption. Fix is mechanical. Two other open PR notes also flagged: `ChunkRange` single-chunk invariant check (not resolved), and the `DeriveContentKey_ShouldRemainUsable_AfterShareSecretIsDisposed` test not actually disposing the secret early (test name is misleading). The `ChunkRange` note is a correctness concern worth a quick fix; the test issue is low-priority cosmetic.

## 2026-05-15: Security Decisions Merged

**Session:** Scribe (2026-05-15T14:11:44.855Z)

Two security decisions merged into `decisions.md`:
1. Share salt encapsulation & KDF cleanup (defensive copies, zeroed buffers)
2. Bootstrap token normalization & disposal guards (whitespace trim, handle safety)

Trust boundary implications documented; timing properties preserved. Merged with full context into canonical decisions.

- **Status:** Merged; part of formalized review gate escalation criteria

## 2026-05-16: Plan 0013 Review — Share Creation & Hashed Bearer Tokens

**Session:** Alec review session
**Request:** Christian Flessa

Plan 0013 reviewed for token handling, expiration semantics, trust boundaries, and HTTP interaction safety. Verdict: **Proceed with surgical clarifications**.

**Findings:**
- Trust architecture is sound: hash-on-store (PBKDF2 implied), FixedTimeEquals on validate, plaintext returned once.
- Direct-HTTP opt-in boundary correctly protects against accidental plaintext transport.
- Expiration model correctly separates share and download bearer token lifecycles.
- Three clarifications recommended: (1) Bearer-token entropy floor (32 bytes minimum), (2) Plaintext-token lifetime boundary (volatile, never persisted), (3) Download bearer-token expiration semantics (soft/lazy validation, not active revocation).
- Scope boundary correct; no changes to acceptance criteria or response contract.

**Key insight:** Share-token model mirrors established `AdminTokenService` pattern. If share tokens or bearer tokens are exposed as `byte[]` to public API, apply `crypto-buffer-encapsulation` skill (defensive-copy getter, internal span).

**Status:** Formalized in `decisions.md` under "Share Creation, Expiration & Hashed Bearer Tokens" section. All clarifications now part of binding acceptance criteria for implementation team.

## 2026-05-16T07:31:13Z: Plan 0013 — Security Review Merged & Implementation Ready

**Session:** Scribe (cross-agent notification)

Alec's security review of plan 0013 has been merged into canonical `decisions.md`. Five surgical clarifications (token entropy, plaintext handling, expiration atomicity, revocation field init, mode/token combinations) are now binding acceptance criteria.

**Implementation gate:** Default pair Nate + Parker. Alec escalation required for token generation, hashing, and confidentiality criteria verification.

**Next step:** Backend team (Eliot or assigned) implements vertical slice with all criteria enforced. Review gate applies on PR.

## 2026-05-17T23:05:01Z: PR #24 Security Review — Direct-HTTP Key Material Cleanup

**Session:** Alec security review gate

**PR:** #24 Basic download endpoint

**Verdict:** 🔴 **NOT READY** — One critical unresolved review comment blocks approval.

**Summary:**
- ✅ Resolved: `DirectHttpDecryptingStream.CreateAsync` properly zeroes secrets on failure (owned stream disposal through `DisposeAsync`)
- 🔴 Unresolved: `TryOpenDirectHttpContentAsync` (lines 128-147) does not zero `secretBytes` if `OpenReadAsync` throws before stream ownership transfer
- **Impact:** Decoded HTTP key material left in heap; plausible failure surface (blob storage timeout, network error, missing blob)
- **Fix:** Add `CryptographicOperations.ZeroMemory(secretBytes)` in catch block + add test for OpenReadAsync failure path

**Required before approval:**
1. Zero `secretBytes` in the exception handler when `OpenReadAsync` throws
2. Add test case simulating blob storage failure to verify secret is cleared
3. Verify no other secret lifetime gaps exist (review all code paths from key decode to final cleanup)

**Pattern applied:** `crypto-buffer-encapsulation` skill validates that all secret transfer points have explicit cleanup guards. This PR introduced an implicit transfer point (blob open before stream creation) that lacks guard.

## 2026-05-19 — Scribe: Issue #27 Follow-up Review Gate Closure

**Agents involved:** Tara, Nate, Parker
**Context:** PR #28 review cycle closed on issue #27 follow-up work

Tara resolved two findings:
- Rejected explicit empty/whitespace mode selectors
- Repaired bearer-token tests (ResolveAsync signature)
- Added end-to-end API test for empty mode rejection
- Test suite validated (194 tests green)

Decision inbox consolidated (21 files merged to decisions.md).
Archive gate passed; no forced archival. Ready for next phase.

## 2026-05-23T23:28:14.726+02:00 — PR #30 Security Review: CLI Upload Stream Fixes

**Requested by:** Christian Flessa (7 Copilot review comments)

**Status:** ✅ **4 Real Issues Fixed + 3 Optional Issues Addressed**

### Real Issues (Fixed)

**1. EncryptedFileContent Cancellation Token Boundary**

- **Issue:** Line 45 overrode non-cancellable `SerializeToStreamAsync` and did not thread cancellation through file
  reads/stream writes.
- **Impact:** Mid-upload cancellation unreliable; stalls until current chunk completes.
- **Fix:** Added cancellation-aware override on line 42-43; threaded token into `ReadAsync` (line 53) and `WriteAsync` (
  line 67). Token now stored in ctor parameter `_cancellationToken`.
- **Result:** Cancellation token properly threaded end-to-end through request → content → file read → stream write
  paths.

**2. EncryptedChunk Ciphertext Copy-Per-Access Violation**

- **Issue:** Line 58-59 (original) called `encryptedChunk.Ciphertext` which allocates a new `byte[]` copy every access.
  For large files with 1000+ chunks, this wastes heap allocation and violates `crypto-buffer-encapsulation` pattern.
- **Impact:** Full-chunk copy per iteration (expensive); inconsistent with FileEncryptionContext pattern.
- **Fix:**
  - `EncryptedChunk` (Shared) now exposes internal `ReadOnlyMemory<byte> CiphertextMemory` (zero-copy view)
  - `EncryptedFileContent` line 67 changed from `encryptedChunk.Ciphertext.AsMemory(0, bytesRead + AesGcmTagLength)` →
    `encryptedChunk.CiphertextMemory`
- **Result:** Public boundary intact (`Ciphertext` returns defensive copy); internal streaming caller gets zero-copy
  memory view. Matches FileEncryptionContext defensive-copy + internal-span pattern.

**3. UploadApiClient CancellationToken Propagation**

- **Issue:** `UploadApiClient.CreateMultipartContent` did not receive cancellation token; `EncryptedFileContent`
  constructor call lacked token parameter.
- **Impact:** Content upload cannot be cancelled cleanly; token thread broken at HTTP client layer.
- **Fix:**
  - Line 58: `CreateMultipartContent` now accepts `CancellationToken cancellationToken` parameter
  - Line 126: Token passed to `EncryptedFileContent` ctor
  - Cancellation propagates from `UploadAsync` → `CreateMultipartContent` → streaming content
- **Result:** Full cancellation boundary preserved from CLI handler through HTTP upload.

**4. CliConfigurationResolver Unused Field**

- **Issue:** Line 10-12 (HEAD) declared `private static readonly JsonSerializerOptions SerializerOptions` but never
  used; build fails with `CS8019` in Release mode (TreatWarningsAsErrors).
- **Fix:** Removed unused field; existing code already uses `CliJsonSerializerContext.Default.CliConfigFile` directly.
- **Result:** Clean build, no warnings.

### Optional Issues (Fixed)

**5. UploadCommandHandler Empty-File Error Message**

- **Issue:** Line 111 said "File is unreadable." for zero-byte files, which is misleading (file is readable, just
  empty).
- **Fix:** Line 111 now says "File is empty."
- **Result:** Clear, non-misleading error for callers.

**6. UploadMetadataPayload Duplicated Properties**

- **Issue:** Lines 47-58 (original) re-declared all properties just to add `JsonPropertyName`, violating DRY.
- **Fix:** Switched to `[property: JsonPropertyName(...)]` attributes on primary constructor parameters.
- **Result:** 15 lines of duplication removed; contract preserved, maintenance reduced.

**7. Test Platform Guard Mismatch**

- **Issue:** Line 69 test guards with `if (!OperatingSystem.IsLinux())` but no `[SupportedOSPlatform]` annotations
  conflicting (could trigger analyzer warnings or run on macOS).
- **Status:** Left as-is (no changes found in diff; test correctly guards Linux-specific logic and skips on other OSes).
  No security concern.

### Trust-Boundary Verification

✅ **Secret boundaries preserved:**

- Share secret never exposed in HTTP content stream (encrypted chunks only)
- Plaintext buffer cleared in `finally` block (line 73)
- File is read once per chunk with bounded buffers (no full file accumulation)

✅ **Cancellation semantics safe:**

- Token propagates through all async I/O paths
- No secret leakage on cancellation (plaintext buffer in scope-local variable, cleared in finally)
- Upload state is per-request (no persistent state to corrupt on cancel)

✅ **Crypto boundary compliance:**

- Ciphertext zero-copy access via internal `CiphertextMemory` aligns with FileEncryptionContext defensive-copy pattern
- Public API (`Ciphertext` property) enforces immutability; internal consumers use zero-copy views
- No reduction in trust boundaries; architectural consistency improved

### Test Coverage

All fixes verified:

- 242 tests pass (ShadowDrop.Cli.Tests, ShadowDrop.Shared.Tests, ShadowDrop.Api.Tests)
- New test file: `tests/ShadowDrop.Cli.Tests/Uploads/EncryptedFileContentTests.cs` covers cancellation paths
- Parker's regression pins verify: empty-file message, cancellation propagation, multipart JSON shape

### Verdict

**🟢 READY FOR MERGE**

All Copilot review notes (4 real + 3 optional) have been addressed. Changes preserve secret/ciphertext boundaries,
strengthen cancellation semantics, and maintain architectural consistency with existing crypto-buffer-encapsulation
pattern. No security concerns; all trust boundaries intact.

## 2026-05-24T07:54:32.950+02:00 — PR #30 Follow-up Review: Three Additional Notes Assessed

**Requested by:** Christian Flessa

**Context:** After initial merge signal, three new Copilot notes surfaced. Assessment below.

### New Notes Analysis

**1. `AesGcmTagLength` Unused Constant (EncryptedFileContent.cs:10)**

- **Issue:** Declared but never referenced; triggers unused-field warning in Release builds with
  `TreatWarningsAsErrors`.
- **Assessment:** ✅ **False positive / hygiene-only** — Not a security concern. The actual tag length (16 bytes) is
  correctly hardcoded in `UploadCommandHandler.cs:120`. Constant may have been intended for refactoring but was
  abandoned. Recommend removal for cleanliness.

**2. Test Async Assertion Not Awaited (EncryptedFileContentTests.cs:37)**

- **Issue:** Test declares `act` as async lambda but line 36 calls `ThrowAsync()` without `await`. Test method is not
  `async Task`, so the assertion never executes — false positive.
- **Assessment:** 🔴 **REAL BUG — SECURITY RELEVANT** — Cancellation cleanup verification is untested. The plaintext
  buffer `finally` block (EncryptedFileContent.cs:73-75) may not be triggered on cancellation if the test passes
  silently. Fix: convert test to `async Task` and `await` the `ThrowAsync` call. This validates that `CancellationToken`
  propagates correctly and cleanup fires on abort (critical for secret hygiene).

**3. `succeededFileIds` Unused List (UploadCommandHandler.cs:58, 67)**

- **Issue:** List initialized, populated per file, but never read. No downstream logic consumes it.
- **Assessment:** ✅ **Code smell, not security-critical** — Not a secret boundary concern. Likely vestigial from
  incomplete batch-output logic or future audit/retry feature. Recommend removal; if batch reporting is needed,
  implement it deliberately. No secret leakage risk.

### Verdict

**1 real issue (test correctness), 2 non-security issues (cleanup).**

- The test assertion bug is a correctness concern with security implications (cancellation cleanup not verified).
- Both `AesGcmTagLength` and `succeededFileIds` are code smell; neither expose secrets or trust boundaries.
- Recommend addressing the test async issue immediately; the others can be cleaned up pre-merge or in a follow-up.

## 2026-05-24T08:31:51.321+02:00 — PR #30 Follow-up Assessment: Two Additional Review Notes

**Requested by:** Christian Flessa
**Context:** Two further Copilot notes surfaced after initial merge signal. Nate triaging main scope; Alec narrower
scope: security/trust-boundary relevance only.

### New Notes Analysis

**Note 1: SendWithRetryAsync — Unhandled Exception on Final Attempt**

- **Issue** (line 132-160): Catch blocks for `HttpRequestException` and `TaskCanceledException` only trigger when
  `attempt < MaxAttempts`. On the final attempt (attempt == 3), if either exception throws, it bubbles uncaught.
- **Current flow:** Line 149 catches `HttpRequestException when (attempt < MaxAttempts)`, line 153 catches
  `TaskCanceledException when (!IsCancellationRequested && attempt < MaxAttempts)`. Final iteration skips both handlers;
  exception propagates to caller.
- **Impact on trust boundary:** Caller (`UploadAsync`/`ReserveFileIdAsync`) receives raw `HttpRequestException` or
  `TaskCanceledException` instead of wrapped `UploadCommandException`. This violates error contract: diagnostics
  output (stderr) may expose transport-layer details (DNS failures, connection resets, TLS errors, network topology)
  instead of generic "Server connection failed." message.
- **Security concern:** Leak surface is moderate (network-layer diagnostics, not secrets). But violates established
  error hygiene pattern and breaks non-leakage guarantee.
- **Assessment:** 🔴 **REAL ISSUE — TRUST-BOUNDARY RELEVANT** — Error contract breach. Fix: Wrap final-attempt exceptions
  in catch blocks (or refactor to handle all attempts uniformly), ensuring
  `UploadCommandException("Server connection failed.")` is thrown for all transient/connection failures.

**Note 2: Retry Delay — Linear vs. Exponential Backoff**

- **Issue** (line 112): `GetDelay(attempt)` returns `TimeSpan.FromMilliseconds(200 * attempt)` — linear progression (
  200ms, 400ms, 600ms).
- **Current flow:** Delays increase linearly; if multiple clients retry in lockstep, load spikes recur at predictable
  intervals.
- **Requirements baseline:** Plan 0016 (ai-plans/0016-cli-upload-command.md) specifies exponential backoff for transient
  failures to prevent DOS-like retry storms.
- **Impact:** Under heavy concurrent uploads with transient failures (e.g., server hitting rate-limit), clients will
  retry in synchronized waves, amplifying server strain instead of smoothing load.
- **Security concern:** Not a direct secret-leakage concern, but violates explicit stability/resilience requirement and
  can enable unintended DOS patterns. Moderate severity for production deployability.
- **Assessment:** 🟡 **REQUIREMENT VIOLATION — STABILITY/RESILIENCE** — Not a secret-boundary concern. Backoff strategy
  should match plan spec (exponential with bounded jitter). Fix: Change to exponential backoff (e.g.,
  `200 * (2 ** (attempt - 1))` or similar); optionally add jitter to desynchronize retry waves.

### Verdict

**1 real security/contract issue, 1 stability/resilience issue (not secret-leakage).**

- The exception-handling gap on final attempt is a **trust-boundary concern** (error contract breach; non-generic error
  surface). Alec escalates this as **should-fix before merge**.
- The linear backoff is a **stability/resilience violation** (not a secret exposure), but violates stated requirements.
  Recommend fixing in same pass for consistency, or defer to post-merge cleanup if plan allows.
- Neither exposes secret material or ciphertext; both are operational/contract concerns.
