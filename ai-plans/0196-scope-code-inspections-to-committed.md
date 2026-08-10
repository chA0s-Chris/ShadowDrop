# Scope code inspections to committed branch changes

> Issue: [#196](https://github.com/chA0s-Chris/ShadowDrop/issues/196)

## Rationale

`inspect_code.sh` currently scopes semantic inspections to staged, unstaged, and untracked C# files. Once those changes are committed, a clean branch or stack layer has no files for the script to inspect, while `--all` widens the check to the entire solution and reports unrelated findings.

Add a committed-diff mode so branch and stack review can inspect the relevant committed C# changes together with any current working-tree changes, without weakening the focused implementation workflow or requiring a whole-solution fallback.

## Acceptance Criteria

- [ ] `./inspect_code.sh --base <revision>` inspects C# files added, copied, modified, or renamed in `<revision>...HEAD`, combined with current staged, unstaged, and untracked C# files.
- [ ] Unrelated source files are excluded from base-aware inspection.
- [ ] Missing, duplicate, or unresolvable `--base` arguments, and `--base` combined with `--all`, exit non-zero with an actionable diagnostic before ReSharper is invoked.
- [ ] Default working-tree mode and explicit `--all` mode retain their current behavior, including forwarding remaining ReSharper arguments.
- [ ] An empty scoped file set reports `No matching files to process.` without widening the inspection.
- [ ] Automated shell coverage verifies selection, argument forwarding, invalid inputs, and mode behavior, and CI runs that coverage.
- [ ] Root `AGENTS.md` documents default, base-aware, and whole-solution usage, including that no changed C# files means inspection is not applicable.

## Technical Details

Treat `--base` and `--all` as script-owned, mutually exclusive modes. Accept `--base <revision>` or `--base=<revision>`, at most once in total, remove the selected mode arguments before invoking `dotnet jb inspectcode`, and continue forwarding every other argument unchanged. The equals form matters because `inspectcode` ignores an unknown option that carries its own value, so a forwarded `--base=main` would silently inspect the working tree instead.

Resolve the supplied revision to a commit before invoking ReSharper, then collect committed paths from the triple-dot diff between that commit and `HEAD` with the `ACMR` diff filter; without copy detection enabled, a copied file surfaces as an addition. Union those paths with the existing staged, unstaged, and untracked selection, retain only C# files, deduplicate them, and build the existing `--include` masks. Leave the current default selection, whole-solution behavior, report handling, and advisory finding semantics unchanged.

Add a focused `scripts/test-inspect-code.sh` regression suite following the repository's existing shell-test pattern. Exercise the real script inside temporary Git repositories with a fake `dotnet` executable so tests can assert the selected include masks and forwarded arguments without running ReSharper. Cover committed additions, modifications, and renames; concurrent working-tree changes; unrelated and deleted files; empty selections; default and `--all` behavior; and invalid or conflicting mode arguments. Run the suite from `.github/workflows/ci.yml` alongside the existing tooling-script validation.

Update root `AGENTS.md` to keep the default invocation after `cleanup_code.sh` and before committing, use `--base` for committed branch and stack review, and reserve `--all` for explicit whole-solution audits, analyzer or configuration changes, and broad refactors. A scoped run with no C# files is not applicable and must not fall back to `--all`.
