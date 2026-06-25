## Rationale

ShadowDrop needs repeatable release packaging targets in the NUKE build so local and CI publishing use the same entry
points. Native AOT publishing is tied to platform toolchains, so CLI release artifacts should be built on the runner
family that owns each target platform and then collected into one release-artifact bundle. This plan adds explicit API
and platform-specific CLI publish targets, plus a GitHub workflow that builds and collects release artifacts without
creating a GitHub Release yet. This plan addresses
[#69](https://github.com/chA0s-Chris/ShadowDrop/issues/69).

## Acceptance Criteria

- [x] `PublishApi` publishes `src/ShadowDrop.Api/ShadowDrop.Api.csproj` with Release configuration to
  `artifacts/publish/api/` after `Restore`, without requiring Docker to be installed.
- [x] `PublishCliLinux` publishes `linux-x64` and `linux-arm64` CLI binaries on an Ubuntu runner, with documented
  Native AOT prerequisites for the ARM64 cross linker.
- [x] `PublishCliMacOs` publishes `osx-x64` and `osx-arm64` CLI binaries on macOS runners.
- [x] `PublishCliWindows` publishes `win-x64` and `win-arm64` CLI binaries on Windows runners.
- [x] Each platform-specific CLI publish target places binaries under `artifacts/publish/cli/{version}/` using
  `shadowdrop-cli-{version}-{rid}[.exe]` names, without requiring that one machine can build every RID.
- [x] A release-artifacts GitHub workflow builds the API and all CLI flavors using appropriate runner jobs, uploads each
  job's outputs as workflow artifacts, and has an aggregation job that downloads every output.
- [x] The release-artifacts workflow creates a `CHECKSUMS.sha256` file in Unix `<hash>  <filename>` format with one entry
  per collected CLI binary and uploads the complete release-artifact bundle.
- [x] The release-artifacts workflow does not create tags or GitHub Releases; actual release publication remains out of
  scope for this plan.
- [x] The new targets are wired into the existing build pipeline without changing the default `Build` entry point.
- [x] Validation proves the platform-specific target graph and workflow artifact layout: API output exists, all six CLI
  binary names are collected, and `CHECKSUMS.sha256` contains exactly one entry per CLI binary.

## Technical Details

Continue the publish target implementation in the existing partial NUKE build file `build/BuildPipeline.Publish.cs`.
The current groundwork already adds `ProjectFileApi`, `ProjectFileCli`, `PublishApiDirectory`, `PublishCliDirectory`,
and `PublishDirectory` helpers to `build/BuildPipeline.Common.cs`, with `PublishDirectory` rooted at
`artifacts/publish`. Reuse these members, plus `SourceDirectory`, `TargetBuildConfiguration`, and `SemanticVersion`,
instead of introducing parallel path or version plumbing.

`PublishApi` is already implemented and verified: it depends on `Restore`, publishes
`src/ShadowDrop.Api/ShadowDrop.Api.csproj` into `artifacts/publish/api/`, and does not require Docker. This output is
suitable for the Dockerfile from #19 to copy into its runtime stage or for a later Docker build target to consume.

The Docker image itself remains out of scope for this plan and will be implemented as part of #19. This plan should only
create the API publish output that #19 can consume. For CLI distribution, this plan owns the NUKE publish-target,
workflow orchestration, and artifact-layout work that overlaps with, and may partially replace, the earlier Native AOT
CLI publishing plan in #20.

The all-RID `PublishCli` shape has been replaced with platform-specific targets. The implementation uses a shared helper
for the common `dotnet publish` invocation, artifact naming, executable copying, and Unix executable-bit handling, while
keeping the public targets aligned with runner capabilities:

- `PublishCliLinux`: publishes `linux-x64` and `linux-arm64`. The Ubuntu job should install Native AOT prerequisites
  before invoking this target, including `clang`, `zlib1g-dev`, `gcc-aarch64-linux-gnu`, `g++-aarch64-linux-gnu`, and
  `binutils-aarch64-linux-gnu`.
- `PublishCliMacOs`: publishes macOS CLI artifacts from macOS runners. If a single macOS runner cannot reliably build
  both `osx-x64` and `osx-arm64`, split this further into RID-specific targets or matrix jobs while preserving the same
  artifact naming contract.
- `PublishCliWindows`: publishes Windows CLI artifacts from Windows runners. If `win-arm64` requires a distinct Visual
  Studio workload or runner setup, keep the target explicit about that prerequisite and let the workflow run the RID on a
  capable runner.

Each CLI publish target should emit flattened release artifacts under `artifacts/publish/cli/{version}/` using
`shadowdrop-cli-{version}-{rid}[.exe]`. Do not generate final checksums inside each platform job, because each job only
sees a subset of artifacts. Instead, checksum generation belongs to the workflow aggregation job after all CLI artifacts
have been downloaded into one directory.

The GitHub workflow `.github/workflows/release-artifacts.yml` can be triggered manually. It should continue to avoid
actual release publication while providing a complete release-artifact bundle:

- Set up .NET 10 in each job.
- Run `PublishApi` on Ubuntu and upload the API publish directory as a workflow artifact.
- Run the Linux, macOS, and Windows CLI publish targets on appropriate runner jobs and upload their CLI artifacts.
- Use a final aggregation job that depends on all publish jobs, downloads all workflow artifacts, verifies that the API
  output and all six CLI binaries are present, writes `CHECKSUMS.sha256` for the six CLI binaries in Unix format, and
  uploads the complete release-artifact bundle.
- Avoid creating tags, GitHub Releases, or release notes in this slice; actual publication can be added later once the
  artifact workflow is stable.

Keep `BuildPipeline.Main()` targeting `Build` so the developer default remains unchanged. A local convenience `Publish`
target may remain useful for API plus host-supported CLI publishing, but it must not be the only way CI attempts to build
all CLI flavors.
