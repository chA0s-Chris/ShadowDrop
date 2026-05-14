# Project Context

- **Owner:** Christian Flessa
- **Project:** ShadowDrop
- **Stack:** C#/.NET, ASP.NET Core, System.CommandLine, Spectre.Console, LiteDB, Docker, Native AOT
- **Created:** 2026-05-14

## Learnings

- Initial role seeded as Platform Dev for ShadowDrop.
- The concept requires one-container Docker distribution plus x64 and arm64 delivery for server and CLI artifacts.
- 2026-05-14T20:06:45.536+02:00: `ShadowDrop.slnx` already contains the three production projects plus all three test projects, so issue work should preserve existing solution membership instead of recreating it.
- 2026-05-14T20:06:45.536+02:00: Baseline dependency wiring is centralized in `Directory.Packages.props`; `LiteDB` is scoped to `src/ShadowDrop.Api/ShadowDrop.Api.csproj`, while `System.CommandLine` and `Spectre.Console` are scoped to `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj`.
- 2026-05-14T20:06:45.536+02:00: Native AOT visibility for the CLI lives in `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj` via `IsAotCompatible`, `PublishAot`, and `InvariantGlobalization`, with `linux-x64` smoke publish as the baseline validation path.

## Squad Transition — 2026-05-14T18:13:02Z

Issue #1 decision (dependency & AOT strategy) merged to team decisions by Scribe.
