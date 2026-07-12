# Add CLI Update Availability Notifications

> Issue: [#133](https://github.com/chA0s-Chris/ShadowDrop/issues/133)

## Rationale

Give ShadowDrop CLI users a lightweight way to discover newer stable releases without introducing an in-process binary replacement mechanism. The CLI should offer an explicit update check and may unobtrusively notify interactive users after ordinary commands, while leaving installation to the existing, platform-specific installer scripts. Automatic network activity must be infrequent, non-blocking from the user's perspective, easy to disable, and documented. Package-manager-specific update guidance is out of scope until ShadowDrop is distributed through a package manager.

## Acceptance Criteria

- [x] The CLI can query the official ShadowDrop release source and compare the latest stable, non-draft release with its installed semantic version.
- [x] An explicit `update` command reports whether the installed version is current and exits zero after a successful check, whether current or update-available; a timeout, malformed release response, or release-service failure writes an actionable diagnostic to stderr and exits nonzero.
- [x] When an update is available, the `update` command displays the installed and latest versions and prints the official Windows or Linux/macOS installation command as appropriate.
- [x] Update guidance reuses the existing `install.ps1` and `install.sh` installation paths and never automatically downloads, executes, or replaces the running CLI binary.
- [x] After an eligible ordinary command completes, when the cached check indicates a newer stable release, the CLI writes a one-line notification to interactive stderr directing the user to `shadowdrop update`.
- [x] Automatic checks are cached and contact the release source no more than once per 24 hours.
- [x] Automatic checks use a short timeout, remain silent on network or release-service failure, and never change the success or failure result of the invoked command.
- [x] Automatic checks and notifications are suppressed for CI and any non-interactive standard stream, unless the update command is explicitly invoked.
- [x] Automatic checks and notifications are not triggered for help, `--version`, parse failures, the `update` command itself, or invocations that request `--json`; successfully parsed ordinary commands are eligible.
- [x] Users can disable automatic checks with a documented `SHADOWDROP_NO_UPDATE_CHECK=1` environment variable.
- [x] The CLI never advertises prerelease releases; a prerelease installation is notified only when a stable release with a higher version exists, and semantic-version comparison orders prerelease versions per SemVer 2.0.0.
- [x] Automated tests cover version comparison, release responses, cache freshness and expiry, opt-out and CI behavior, failure isolation, and platform-specific installation guidance.
- [x] The CLI guide documents the update command, automatic-check behavior, network/privacy implications, cache interval, and opt-out environment variable.

## Technical Details

Add an `update` command to the command model and dispatch flow in `CliApplication`. Keep the command handler and update-specific types in a dedicated CLI feature namespace, and register their dependencies through `CliApplicationServices` so tests can replace network, environment, terminal, filesystem, platform, and time concerns without making live release requests.

Use an `HttpClient`-backed release client to request the latest stable ShadowDrop release from the official GitHub releases API. Send an appropriate user agent, apply a short update-specific timeout, parse only the required release fields using source-generated JSON metadata compatible with Native AOT, and treat malformed, missing, or draft release data as a failed check. Normalize the release tag's leading `v` prefix before semantic-version comparison, since release tags are published as `vX.Y.Z` while `CliVersion` reports a bare semantic version. Centralize semantic-version parsing and comparison so stable and prerelease behavior is explicit and testable; the version reported by `CliVersion` should remain the installed-version source of truth.

Persist a minimal update-check record in `%LOCALAPPDATA%\ShadowDrop\Cache` on Windows and `$XDG_CACHE_HOME/shadowdrop` on Linux/macOS, falling back to `~/.cache/shadowdrop` when `XDG_CACHE_HOME` is unset. The record should include the check time and enough release information to serve a fresh result without another request. Write cache updates atomically, tolerate missing or corrupt cache files, and use `TimeProvider` to enforce the 24-hour interval deterministically. An explicit `shadowdrop update` invocation may bypass stale cached failure state and should explain request failures, while automatic checks must swallow update-specific failures.

Run automatic checking only after a successfully parsed ordinary command completes; exclude help, `--version`, parse failures, the `update` command, and invocations that request `--json`. Run it only when both standard output and standard error are interactive human-facing streams, and suppress it when a recognized CI environment is present or `SHADOWDROP_NO_UPDATE_CHECK` is enabled. Treat at least the `CI` environment variable being set to a truthy value as a CI environment; additional well-known CI variables (e.g. `GITHUB_ACTIONS`) may be recognized. Preserve machine-readable output contracts, especially `--json`; write update notices only to interactive standard error. The notification should be concise and direct users to the explicit update command rather than printing an installer during unrelated work.

Generate installation guidance from the current operating system and the canonical URLs already documented in `docs/CLI.md`: `install.ps1` on Windows and `install.sh` on Linux/macOS. Keep the guidance behind a small installation-guidance abstraction so a package-manager recommendation can be added later without touching the command handler. Do not invoke a shell, installer, package manager, or downloaded artifact.

Extend `ShadowDrop.Cli.Tests` with fakes or stub HTTP handlers for release responses and focused command-level tests. Cover success, up-to-date, newer stable, prerelease exclusion, timeouts, malformed responses, corrupt caches, cache expiry, environment suppression, redirected output, and Windows versus Unix guidance without depending on GitHub or the host platform during tests.
