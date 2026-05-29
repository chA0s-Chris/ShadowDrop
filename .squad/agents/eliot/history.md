# Eliot — Backend Developer

## Context

- **Role:** Backend Developer for ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, LiteDB, Docker, Native AOT
- **Archive:** Full history (2026-05-14 through 2026-05-28) documented in `history-archive.md`
- **Focus:** Range requests, streaming contracts, header security, resource cleanup

## Recent Session — 2026-05-29

### Cross-Agent Notification

Sophie (CLI Dev) completed plan #17 (CLI download with queue processing and manifest support). All acceptance criteria met.

**Impact to Eliot:**
- Queue manifest endpoint `/d/{token}` is now part of public download contract
- Download responses continue to support per-file queue entries with server/share/file metadata
- No changes to range header handling or streaming contracts in this slice
- Manifest support may affect future plan 0027 (v2 binary contract negotiation)

**Dependencies:** Eliot may be assigned future work on plan 0027 (streamed binary v2) which requires similar header/streaming rigor

## Learnings

- 2026-05-29T02:49:00.341+02:00 — Queue download entries are part of the public recipient contract and must document `fileId`, `fileName`, and `length` alongside `serverUrl`, `shareId`, and `outputPath`; the CLI uses those manifest-bound fields to fail closed when queued metadata drifts from live share metadata. Key paths: `ai-plans/0017-cli-download-command-and-queue-processing.md`, `src/ShadowDrop.Shared/Queue/QueueFileEntry.cs`, `src/ShadowDrop.Shared/Queue/QueueFileParser.cs`.
- 2026-05-29T02:49:00.341+02:00 — CLI download file selection should compare GUID-like file identifiers by parsed `Guid`, not raw string casing, for both direct `--file` selection and queue entries. Key paths: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`.
- 2026-05-29T02:49:00.341+02:00 — Manifest cache keys should reuse the canonical manifest URI so equivalent server URL variants (such as trailing-slash differences under a base path) share one cache entry. Key paths: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`, `src/ShadowDrop.Cli/Downloads/ShareDownloadUriFactory.cs`.
- 2026-05-29T03:18:53.122+02:00 — Manifest file lookup in the CLI must reject duplicate `fileId` values as invalid share metadata instead of relying on `SingleOrDefault`, so malformed manifests stay on the `DownloadCommandException` path and queue processing can continue per entry. Key paths: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`.

## Review-Fix Pass — 2026-05-29T02:49:00.341+02:00

Addressed PR #31 unresolved review items 1-4:
- Fixed CLI fileId case-sensitivity in comparisons (GUID semantics, not string ordinal)
- Normalized manifest cache keys for server URL variants (canonical URI reuse)
- Updated queue-file contract docs to list full per-file metadata
- Added regression tests for fileId matching and manifest cache
- Posted 3 GitHub review replies; top-level reply had no usable API path

All changes pushed. Decision on queue contract and cache normalization recorded to team decisions.md.

## Review-Fix Second Pass — 2026-05-29T03:18:53.122+02:00

User directed Scribe to address remaining PR #31 actions:
- **Item 1 skipped** per user choice (case-insensitive GUID work deferred)
- **Item 2 implemented** (duplicate manifest fileId defense-in-depth)
  - Added fail-closed validation in `DownloadCommandHandler` to treat duplicate `fileId` as invalid metadata
  - Regression test added to `DownloadCommandHandlerTests`
  - GitHub review thread reply posted with implementation rationale
  - Decision recorded: `eliot-duplicate-manifest-file-ids.md` → decisions.md
- **Item 3 pending** (documentation updates for plan 0017 — assigned to Ralph/Scribe)
- 2026-05-29T07:28:43.247+02:00 — Queue downloads should treat the queue file's validated `serverUrl` as authoritative and parse it directly instead of reopening CLI config for every entry; malformed local config must not break explicit queue-driven downloads. Key paths: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`.
- 2026-05-29T07:28:43.247+02:00 — Per-entry queue download status belongs on stderr as each entry completes so long runs surface partial progress immediately and interrupted runs keep already-emitted results. Key paths: `src/ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs`, `tests/ShadowDrop.Cli.Tests/Downloads/DownloadCommandHandlerTests.cs`.

## 2026-05-29T07:28 — PR #31 review-fix captured to team decisions

- Addressed review items 2 & 3 per user choice
- Item 2: Queue processing now uses validated entry serverUrl directly
- Item 3: Queue execution emits SUCCESS/FAILED per entry
- Regression coverage updated in tests
- Decision merged: `eliot-queue-download-runtime-behavior.md`
- Orchestration log: `2026-05-29T07-28-43-eliot.md`
