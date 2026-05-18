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

## 2026-05-18 09:19:54 UTC — Range Request Implementation Session

- Joined team deployment for issue #15
- Coordinate cross-agent work on HTTP range support
- All agents operational and focused
