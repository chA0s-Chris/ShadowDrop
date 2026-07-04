# Remove Squad From Repository

## Rationale

Issue: [#108](https://github.com/chA0s-Chris/ShadowDrop/issues/108)

The repository should no longer carry Squad/Copilot-agent configuration because the project is moving away from that workflow. Remove the Squad-specific files, Copilot skill bundle, workflow entries, ignore rules, MCP configuration, and generated GitHub labels so future development does not present or maintain obsolete automation paths.

## Acceptance Criteria

- [x] The `.squad/` directory is removed from the repository.
- [x] The `.copilot/` directory is removed from the repository.
- [x] `.github/agents/squad.agent.md` is removed from the repository.
- [x] Any `.github/workflows/*squad*.yml` workflow files are removed from the repository.
- [x] Squad-specific entries are removed from `.gitignore`.
- [x] `.github/copilot-instructions.md` is kept, but its `./.squad` ignored-directory entry and entire RTK section are removed.
- [x] `.mcp.json` is removed from the repository.
- [x] The GitHub label named `squad` and every GitHub label whose name contains `:` are removed from the repository.
- [x] Repository validation is run with `rtk ./build.sh` after the cleanup, and any failures caused by the removal are addressed.

## Technical Details

Inspect the repository for Squad-specific paths and references before deleting files so the cleanup covers both explicitly listed artifacts and nearby references that would become stale. Remove the files and directories from version control rather than leaving empty placeholders. Keep `.github/copilot-instructions.md` because it still carries instructions for GitHub Copilot features such as code reviews, but remove its obsolete `.squad` ignore entry and RTK command guidance.

Update `.gitignore` only for Squad-related entries, preserving unrelated ignore rules. Delete generated GitHub labels through the forge CLI after identifying their exact names; label deletion is repository metadata and will not appear in the working tree diff.

After the cleanup, run `rtk ./build.sh` and address failures caused by the cleanup.
