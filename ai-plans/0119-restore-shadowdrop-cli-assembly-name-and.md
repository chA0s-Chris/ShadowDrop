# Restore ShadowDrop.Cli assembly name and rename CLI artifacts during publish

> Issue: [#119](https://github.com/chA0s-Chris/ShadowDrop/issues/119)

## Rationale

The CLI project currently uses `<AssemblyName>shadowdrop</AssemblyName>` so published executables
carry the documented `shadowdrop` command name. That fixes the user-facing binary name, but it also
changes the .NET assembly identity away from `ShadowDrop.Cli`, which excludes the CLI from coverage
collection in GitHub Actions and local coverage tools.

The assembly identity should remain `ShadowDrop.Cli`. Publish and release packaging should be
responsible for renaming the built executable and release artifacts to the documented
`shadowdrop` / `shadowdrop.exe` command names.

## Acceptance Criteria

- [x] Remove `<AssemblyName>shadowdrop</AssemblyName>` from
  `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj`.
- [x] `InternalsVisibleTo` in `src/ShadowDrop.Shared/Properties/AssemblyInfo.cs` grants to
  `ShadowDrop.Cli`, and the stale comments referencing the removed `<AssemblyName>` override are
  deleted from that file, the CLI csproj, and
  `tests/ShadowDrop.E2E.Tests/Infrastructure/ProductArtifacts.cs`.
- [x] The merged Cobertura report contains a `<package name="ShadowDrop.Cli">` entry with non-zero
  line coverage, and the CI coverage summary produced by `CodeCoverageSummary` lists
  `ShadowDrop.Cli` as a row.
- [x] Published CLI artifacts are still named `shadowdrop-<version>-<rid>` and
  `shadowdrop-<version>-<rid>.exe`.
- [x] The installed/user-facing executable remains `shadowdrop` / `shadowdrop.exe`.
- [x] A manual `workflow_dispatch` run of the Release Artifacts workflow on the feature branch
  produces correctly named CLI artifacts for the Linux, macOS, and Windows runtime identifiers.
- [x] `PublishCliArtifacts` fails the publish target if either the published executable
  (`ShadowDrop.Cli` / `ShadowDrop.Cli.exe`) or the renamed release artifact
  (`shadowdrop-<version>-<rid>`) is missing.

## Technical Details

Remove the assembly-name override from `src/ShadowDrop.Cli/ShadowDrop.Cli.csproj` so the CLI assembly
identity returns to the project default, `ShadowDrop.Cli`.

The CLI is currently excluded from coverage because `coverlet.xml` includes `[ShadowDrop.*]*`, which
the assembly name `shadowdrop` cannot match. Restoring the assembly identity fixes this on its own;
`coverlet.xml` itself must not change.

`src/ShadowDrop.Shared/Properties/AssemblyInfo.cs` grants `InternalsVisibleTo("shadowdrop")`, keyed to
the override being removed. The grant is load-bearing — the CLI consumes `ShadowDrop.Shared` internals
(for example `ContentKey.KeyMaterial` and `EncryptedChunk.CiphertextMemory`) — so it must be retargeted
to `ShadowDrop.Cli` in the same change, or the CLI will not compile. Delete the comments explaining the
old assembly name in that file, in the CLI csproj, and in the E2E artifact builder (see below).

Update the NUKE publish pipeline in `build/BuildPipeline.Publish.cs` so artifact naming is not
coupled to the assembly identity. After the assembly-name override is removed, `dotnet publish`
should emit `ShadowDrop.Cli` or `ShadowDrop.Cli.exe` depending on the runtime. The publish target
should locate that generated executable, then copy or rename it to the desired release artifact name:
`shadowdrop-<version>-<rid>` on Linux/macOS and `shadowdrop-<version>-<rid>.exe` on Windows.

`CliExecutableName` currently serves two roles that this change pulls apart: the name `dotnet publish`
emits, and the release artifact prefix. Split it — keep `CliExecutableName = "shadowdrop"` for
`GetCliArtifactName`, and add `CliPublishedAssemblyName = "ShadowDrop.Cli"` for
`GetCliPublishedExecutableName`. The two names are then independent, so the csproj no longer needs a
"keep in sync" comment. Review checksum generation and release workflow glob patterns against the same
distinction.

Only the assembly identity changes. The config-directory name
(`CliConfigPathConstants.ApplicationDirectoryName`), the Docker image repository
(`DockerImageRepository`), and the release artifact prefix (`CliExecutableName`) all remain
`shadowdrop`.

`PublishCliArtifacts` already throws `FileNotFoundException` when the published executable is missing,
which becomes the regression guard for the publish-output name once `GetCliPublishedExecutableName`
returns `ShadowDrop.Cli`. Add the mirror assertion after the copy so a missing or misnamed release
artifact also fails the target. No build-pipeline test project is needed.

Keep the documented install flow intact. Users should still end up with an executable named
`shadowdrop` or `shadowdrop.exe`; only the internal assembly identity and publish-pipeline rename
boundary should change.

Update `tests/ShadowDrop.E2E.Tests/Infrastructure/ProductArtifacts.cs` so the E2E artifact builder
expects the CLI build output assembly to be `ShadowDrop.Cli.dll` instead of `shadowdrop.dll`.

Validate the change with the normal build/test path. Because the CLI publishes with `PublishAot`, a
local publish only covers the host runtime identifier; cross-platform coverage comes from the Release
Artifacts workflow, which publishes each runtime-identifier group on its own runner. That workflow does
not run on pull requests, so verify it with a manual `workflow_dispatch` run on the feature branch.
