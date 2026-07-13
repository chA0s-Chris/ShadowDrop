# Replace raw GitHub installer URLs with get.shadowdrop.net

> Issue: [#146](https://github.com/chA0s-Chris/ShadowDrop/issues/146)

## Rationale

Replace the implementation-facing raw GitHub installer URLs with stable, project-owned `get.shadowdrop.net` URLs so installation and update instructions use the ShadowDrop domain while the redirect continues to serve the scripts from the repository's `main` branch.

## Acceptance Criteria

- [ ] Unix install examples and CLI update guidance use `https://get.shadowdrop.net/install.sh`.
- [ ] PowerShell install examples and CLI update guidance use `https://get.shadowdrop.net/install.ps1`.
- [ ] The default-directory and custom-directory command forms remain unchanged apart from their installer URLs.
- [ ] `README.md` and `docs/CLI.md` consistently present the branded installer URLs.
- [ ] Automated tests assert the branded URLs for direct installation guidance, update-command output, and the CLI update flow, and all affected tests pass.
- [ ] No previous raw GitHub installer URL remains in production code, user-facing documentation, or active test expectations; any retained occurrence is intentional historical context and is documented as such.

## Technical Details

Update `ShadowDrop.Cli.Updates.InstallationGuidanceProvider` so its shared installer URL source points at `https://get.shadowdrop.net`, renaming the existing `RepositoryMain` constant if needed to reflect that it is now an installer endpoint rather than a repository branch URL. Continue deriving the Unix and Windows script URLs from that single source of truth and preserve the existing default and install-directory-specific command construction, quoting behavior, and platform selection.

Replace the one-line install and update examples in `README.md` and `docs/CLI.md` with the corresponding branded script URLs. Update the existing FluentAssertions expectations in `InstallationGuidanceProviderTests`, `UpdateCommandHandlerTests`, and `CliApplicationUpdateTests` so they verify the new URLs at the provider, handler, and application boundaries without weakening their current command-shape coverage.

Verify the repository for remaining occurrences of the old raw GitHub installer prefix after the changes. Completed plans such as issue 113 may retain the old URL as a historical description of the implementation delivered at that time; such references are not active installation guidance and should only be changed if the repository's plan-history convention requires retrospective updates.
