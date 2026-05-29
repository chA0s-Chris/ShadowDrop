# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as CLI Dev for ShadowDrop.
- The concept requires both fully parameterized commands and wizard-like terminal flows.
- **Plan 0018 refinement (2026-05-16):** Interactive UX demands rigorous clarity on secret boundaries, input constraints, and mode-switching rules. Share-mode invalid combinations (direct-HTTP + bearer token, separate-key without token setup) must be enforced in the interactive layer, not just the API. Bearer-token entry in interactive mode is strictly masked terminal input—no fallback to env/config. Share-key input during download accepts hybrid precedence (CLI flags override prompting), which bridges scripted and guided workflows. Clipboard/save-to-file mechanisms for secrets are explicitly out of scope; users retain control via shell redirection and external tools. This keeps the secret-handling surface tight and auditable.
- **Cross-agent: Scribe reconciliation (2026-05-16T20:41:21Z):** All plan 0018 clarifications merged into decisions.md. Inbox fully processed (13 files → 0). Sophie's work on plan 0018 final clarifications is canonicalized in team decisions.
- **Issue 15 CLI contract (2026-05-18T11:19:54.273+02:00):** Locked the CLI resumable-download shape as JSON with explicit chunk-span and plaintext-range metadata, and kept response-discovery details in stable headers instead of burying filename/content-type inside the encrypted payload contract. Also had to switch the parser onto shared `System.Text.Json` source generation immediately, because the CLI's Native AOT constraint makes reflection-based deserialization the wrong default even for small transport DTOs.
- **PR 29 follow-up (2026-05-19T14:38:07.521+02:00):** `CliDownloadRequestFactory` now treats `mode` as a normalized singleton query parameter: any pre-existing `mode` entries are removed before appending `mode=cli`, so scripted callers cannot accidentally send duplicate or conflicting mode values. Regression coverage lives in `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadRequestFactoryTests.cs`, and the shared metadata smoke test name in `tests/ShadowDrop.Shared.Tests/Contracts/CliDownloadMetadataContractTests.cs` was tightened to match what it actually asserts. Validated with existing CLI/shared test slices.

- **Issue 18 interactive UX (2026-05-29T09:11:29.068+02:00):** `upload --interactive` now owns the guided upload/share flow, while `download --interactive` handles masked share-key and bearer-token prompting plus file selection. The production adapter lives under `src/ShadowDrop.Cli/Interactive/`, the shared upload orchestration moved into `src/ShadowDrop.Cli/Uploads/UploadCommandExecutor.cs`, and the fakeable session pattern for orchestration tests lives in `tests/ShadowDrop.Cli.Tests/Fakes/FakeInteractiveSession.cs`. Help detection now relies on `ParseResult.Action is HelpAction`, which keeps explicit interactive flags compatible with existing `--help` behavior.

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused

## 2026-05-18 23:24:50 UTC — Codebase Impact Analysis for CLI Download Contract Refresh

Inspected codebase to identify surfaces affected by plan 0027 decision: CLI subset selection uses `?mode=cli` + standard `Range: bytes=...` header, retiring legacy `plaintextStart`/`plaintextEndExclusive` query parameters.

### Identified Affected Surfaces

**API Request/Response Boundary:**
- `DownloadEndpoints.cs` (lines 32-43): Currently extracts three separate inputs—Range header, plaintextStart, plaintextEndExclusive—and passes them to service. Will need to parse `?mode=cli` query parameter and enforce Range-only selection for CLI mode.
- `DownloadFileService.ResolveAsync()` method family: Four overloads with varying signatures (lines 34–76). Some accept plaintextStart/plaintextEndExclusive, others omit them. Will consolidate to accept mode parameter and unified range resolution.
- `ResolveRequestedRange()` internal method (lines 391–438): Currently handles mixed inputs (header ranges + query params) with mutual-exclusion logic. Will be refactored to reject mixing and enforce mode-specific syntax.

**Shared Contracts/Constants:**
- `DownloadHeaderConstants.cs`: Will need new headers for CLI binary contract metadata (First-Chunk-Index, Last-Chunk-Index, Plaintext-Range-Start/End, Total-Plaintext-Size, Chunk-Size, Final-Chunk-Plaintext-Length).
- `CliResumableDownloadContract.cs`: Current JSON DTO with Base64 encryptedPayload will be retired. Metadata will move to response headers; payload moves to raw binary body.

**CLI Parser/Consumer:**
- `CliResumableDownloadContractParser.cs`: JSON deserialization parser (lines 20–126) becomes obsolete. Must be replaced with header-reader that validates custom metadata headers before consuming streamed binary body.

**Test Coverage That Will Change:**
- `ApiWalkingSkeletonTests.cs` (lines 620–629): Explicit test for legacy `plaintextStart`/`plaintextEndExclusive` query parameters. Will be retired or converted to use Range headers with mode=cli.
- `DownloadFileServiceTests.cs` (1108 lines): Many tests invoke ResolveAsync with old parameters. All will need signature updates.
- `CliResumableDownloadContractParserTests.cs` (159 lines): JSON parsing tests will be deprecated; new tests must cover header parsing and binary-stream consumption.
- `CliResumableDownloadContractTests.cs`: JSON contract shape tests will be obsolete.

**Response Streaming & HTTP Semantics:**
- `DownloadFileResolution.cs`: Record carries `ResponseContentType` (currently always "application/json" for CLI mode) and `RequestedRange` (used for Content-Range header). Will need to emit custom media type `application/vnd.shadowdrop.cli-download` and shift metadata to headers.
- `DownloadMode` enum: Currently only has DirectHttp and CliDecrypt. May need explicit value or flag to distinguish new vs. legacy CLI contract if backward compat is considered (plan 0027 says remove, so likely no change needed).

**Authentication/Authorization (Stable):**
- Range validation reuses existing plaintext-range-to-chunk-span logic. Share token, bearer-token, expiration checks remain unchanged per plan scope.

### Why These Matter

1. **Endpoint request handling** must explicitly negotiate `?mode=cli` and reject ambiguous inputs (e.g., mixing legacy query params with mode=cli or Range headers).
2. **Service method surface** needs refactoring to accept mode parameter and singular range input, eliminating four overload variants.
3. **Shared constants** will grow to define seven new custom headers for encrypted-subset metadata (chunk indices, range, total size, chunk size, final chunk length).
4. **CLI parser** becomes the critical integration point: must robustly parse headers, validate presence/format, detect truncation before trusting the body.
5. **Test suites** must be comprehensively rewritten—not just updating method calls, but inverting the shape of what "success" means (binary response with headers vs. JSON body).

### Non-Affected Areas

- Direct-HTTP download path remains stable (uses different mode branch).
- Share creation, upload, token generation unchanged.
- Core encryption/decryption and chunk-mapping logic remains the same.

## Session: Team Integration (2026-05-19T13:14:01Z)
Both Eliot and Sophie completed targeted fixes in download service handling.
- Eliot: DownloadFileService range header validation (23 tests passing)
- Sophie: LengthValidatingReadStream disposal fix (CliDownloadResponseParserTests validated)
Ready for integration.
- **Issue 16 upload command (2026-05-23T22:26:23.766+02:00):** The CLI upload path is now intentionally split between
  script-stable stdout and human stderr: successful file ids stream to stdout in argument order, `secret:<hex>` is
  emitted only as the final stdout line on full success behind `--output-secret`, and all diagnostics stay on stderr.
  Config resolution is locked to flags > `SHADOWDROP_SERVER_URL` / `SHADOWDROP_UPLOAD_TOKEN` > config file, with token
  trimming before use and Native AOT-safe JSON source generation for config and upload DTOs.
- **Issue 17 download CLI (2026-05-29T00:41:01.379+02:00):** The recipient download path now hinges on a lightweight
  public share manifest at `GET /d/{token}` plus the streamed CLI download endpoint. Shared queue validation moved to
  per-file entries (`serverUrl`, `shareId`, `fileId`, `fileName`, `length`, `outputPath`, optional `plaintextSha256`),
  which keeps queue files secret-free while letting the CLI batch downloads predictably. Key implementation paths:
  `src/ShadowDrop.Cli/Downloads/`, `src/ShadowDrop.Api/Downloads/`, `src/ShadowDrop.Shared/Queue/`,
  `tests/ShadowDrop.Cli.Tests/Downloads/`.

## 2026-05-29: Issue Prioritization — Next Target: #18 (Interactive UX)

**Event:** Nate prioritized open GitHub issues (#18, #19, #20) and recommended #18 (Interactive Spectre.Console UX) as the next implementation target.

**Assignment:** Sophie leads #18 implementation.
- **Role:** CLI lead on interactive guided workflows
- **Collaborators:** Alec (secret handling review), Parker (orchestration tests)
- **Dependencies:** None — upload (#30), share creation (#23), and download (#31) already merged
- **Scope:** 12 acceptance criteria covering guided workflows, TTY detection, orchestration contracts, and testing

**Why #18 first:**
- Clear acceptance criteria and unambiguous team routing
- Orthogonal to existing slices; no API changes needed
- Unblocks interactive user experience without blocking end-user features
- All underlying operations (upload, share, download) are complete and merged

**Sequencing rationale:** #19 (Docker) and #20 (Native AOT) deferred pending scope clarification from Christian.

**Decision:** Recorded in canonical `.squad/decisions.md`

## 2026-05-29 09:32:00 UTC — Plan #18 Interactive Spectre.Console UX Implementation Complete

Completed full end-to-end implementation of plan #18 via the implement-plan skill.

### Work Completed

- **Branch Created:** `squad/0018-interactive-spectre-console-ux`
- **Core Changes:**
  - Modified `CliApplication.cs` and `CliApplicationServices.cs` for interactive routing and command dispatch
  - Refactored `UploadCommandHandler.cs` with guided workflow: server URL → share mode → file selection → share creation → upload metadata
  - Refactored `DownloadCommandHandler.cs` with interactive prompting for server URL, share ID, share key (masked), bearer token (masked), and file selection
  - Interactive orchestration adapter under `src/ShadowDrop.Cli/Interactive/`
  - Shared upload orchestration extracted to `UploadCommandExecutor.cs` for test reuse
  - Help action detection via `ParseResult.Action is HelpAction` preserves `--help` compatibility with `--interactive`

### Test Status

- **All tests passing:** 271 total via `dotnet test ShadowDrop.slnx`
- Test double framework (`FakeInteractiveSession.cs`) enabled orchestration testing
- Updated upload/download handler tests with interactive flow coverage

### Decision Captured

- **Decision:** Keep guided share-creation under `--interactive` instead of separate CLI command
- **Rationale:** Minimizes public surface, preserves scripting capability, delegates to existing upload/share logic

### Acceptance Criteria Status

- ✓ `--interactive` flag implemented and routed correctly
- ✓ Interactive upload flow with guided share creation complete
- ✓ Interactive download flow with secret masking and file selection complete
- ✓ Help detection compatible with interactive mode
- ✓ All tests passing (271)
- ✓ Plan #18 acceptance criteria marked complete

### Readiness

- Branch ready for review and merge workflow
- No post-implementation blockers identified
- Scribe decision merged into canonical decisions.md

## 2026-05-29T14:29:36.720+02:00 — Issue #20 Finalization (Native AOT CLI)

**Cross-Agent Update:** Issue #20 is now locked and ready for your assignment.

**Final MVP Scope:**

- **RID Matrix:** All 6 confirmed (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64)
- **Artifact Contract:** `artifacts/cli/{version}/`, naming `shadowdrop-cli-{version}-{rid}[.exe]`, single CHECKSUMS.sha256
- **Build Strategy:** NUKE Publish target for unified orchestration
- **Validation Split:** Smoke tests on native targets (linux-x64, osx-x64, osx-arm64) only
- **CI:** GitHub Actions with native macOS runners + Linux cross-compile

**Acceptance Criteria:** 7 concrete, implementation-ready items (blocker protocol removed per user directive)

**Team Readiness:** All decision gates locked. No open gates. Ready for assignment and implementation.

**Next:** Begin with NUKE Publish target, then GitHub Actions matrix per smoke-test selectivity.
