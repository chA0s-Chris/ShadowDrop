# Align published CLI binary/install flow with the documented `shadowdrop` command name

> Issue: [#106](https://github.com/chA0s-Chris/ShadowDrop/issues/106)

## Rationale

The MVP documentation uses `shadowdrop` as the CLI command name, but the current publish pipeline
emits per-platform downloads named `shadowdrop-cli-<version>-<rid>[.exe]`. Users therefore need to
manually rename the downloaded binary before it matches the documented command. Before the first
release, the release artifact and installation flow should produce an executable named `shadowdrop`
(`shadowdrop.exe` on Windows) without requiring hand-crafted user-side renames.

## Acceptance Criteria

- [x] Installing the CLI from a release makes the documented `shadowdrop` command
  (`shadowdrop.exe` on Windows) available through a single documented install/copy step for
  each supported platform — including a Windows equivalent — without the user having to derive
  an undocumented rename of a `shadowdrop-cli-*` file. Release artifacts are named
  `shadowdrop-<version>-<rid>` (`.exe` on Windows).
- [x] The install instructions in `README.md` and `docs/CLI.md` are updated to match the
  implemented flow.
- [x] `CHECKSUMS.sha256` or its successor still covers whatever artifacts users download.
- [x] The selected release/install flow is verified by an automated test, build target, or
  documented manual verification that confirms the downloaded artifact installs/runs as
  `shadowdrop` on at least the current development platform.

## Technical Details

Inspect the existing publish flow in `build/BuildPipeline.Publish.cs`, especially
`GetCliArtifactName`, and the CLI project configuration in `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj`
before choosing the implementation path.

Set the CLI assembly name to `shadowdrop` in `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj` so the
published native executable already carries the documented command name. Keep publishing raw,
runtime-specific binaries (no archives or installers) and change the user-facing artifact naming
from `shadowdrop-cli-*` to `shadowdrop-<version>-<rid>` (`.exe` on Windows) in
`GetCliArtifactName`, so the name still identifies version and runtime and the checksums stay
simple.

The assembly-name change must be mirrored in `GetCliPublishedExecutableName`, which currently
hard-codes `ShadowDrop.Cli{extension}` and is asserted to exist right after `dotnet publish`
(`PublishCliArtifacts`). If it is left pointing at the old name, the publish target throws before it
reaches `GetCliArtifactName`. Ideally derive the published executable name and the assembly name
from a single source so they cannot drift.

The user-facing mechanism is a single documented install/copy step per platform rather than an
archive or install script: on Linux/macOS the existing `install -m 755 <download>
~/.local/bin/shadowdrop` already places the binary under the command name; document an equivalent
one-line step for Windows (e.g. copy the downloaded `.exe` to `shadowdrop.exe` in a PATH
directory). The docs must not leave the rename as an undocumented user responsibility.

Also update `.github/workflows/release-artifacts.yml` and `.github/workflows/release.yml`: both
hard-code `shadowdrop-cli-*` name patterns in the three CLI upload `path:` globs, the collect-step
`find`, the `sha256sum` glob, and the `release.yml` asset-upload list. Only the name patterns change
(`shadowdrop-cli-*` → `shadowdrop-*`); the artifact count is unaffected — there are still six RIDs,
so `expected_count=6` stays as-is.

Update `README.md` and `docs/CLI.md` so the documented install flow matches the implemented
artifact shape. The docs should no longer describe a manual rename as a user responsibility unless
the implemented install mechanism explicitly performs that rename for them.

Make sure the checksum generation still hashes the exact files users download. If the release flow
switches to archives, installers, or scripts, include those downloadable files in
`CHECKSUMS.sha256` or the replacement checksum artifact.
