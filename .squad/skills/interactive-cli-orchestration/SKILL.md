---
name: "interactive-cli-orchestration"
description: "Wrap existing CLI business logic with a fakeable interactive session so Spectre UX stays testable and script-safe."
domain: "testing"
confidence: "high"
source: "earned"
---

## Context

Use this when a CLI needs an explicit interactive mode without weakening existing non-interactive contracts.

## Patterns

- Put `--interactive` on the subcommand so parsing stays orthogonal to scripted usage.
- Route interactive execution through a small session interface instead of calling Spectre directly from business logic.
- Keep Spectre in the production adapter and use a fake session in tests so orchestration assertions stay stable.
- Reuse underlying command/use-case helpers for uploads and downloads; only prompts, summaries, and opt-in secret display belong in the interactive layer.
- Fail immediately with a fixed TTY-required message when terminal capabilities are missing.

## Examples

- `src/ShadowDrop.Cli/Interactive/`
- `tests/ShadowDrop.Cli.Tests/Fakes/FakeInteractiveSession.cs`

## Anti-Patterns

- Reimplementing upload/download/share business rules inside prompt handlers.
- Falling back silently to non-interactive behavior when `--interactive` cannot run.
- Printing secrets in the same channel as normal diagnostics.
