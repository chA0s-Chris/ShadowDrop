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
