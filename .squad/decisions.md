# Squad Decisions

## Active Decisions

- 2026-05-14: The initial ShadowDrop squad uses the user-chosen names Nate, Eliot, Sophie, Alec, Tara, and Parker, with Scribe and Ralph as built-in roles.
- 2026-05-14: ShadowDrop work is routed by specialization across lead, backend, CLI, security, platform, and testing rather than a generic pooled roster.

## Dependency & AOT Strategy

- 2026-05-14: Keep baseline dependency wiring aligned to deployment boundaries; make Native AOT support explicit in CLI project from first slice (Tara #1). Applied in Directory.Packages.props, ShadowDrop.Api.csproj, ShadowDrop.Cli.csproj. Ensures minimal API/CLI package surface, preserves shared-library boundary, enables `linux-x64` publish path for local and CI before feature code accumulates.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
