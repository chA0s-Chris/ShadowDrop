## Rationale

Finish the CLI distribution story by producing publishable binaries for the supported platforms. This slice turns
the earlier Native AOT groundwork into a repeatable release process with concrete artifacts for Linux, macOS, and
Windows on x64 and arm64. MVP delivers all six target binaries, organized in a flat artifact structure with checksums,
deployable directly to GitHub Releases.

## Acceptance Criteria

- [ ] Native AOT `dotnet publish` succeeds for Runtime Identifiers: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`.
- [ ] All publish artifacts are placed in `artifacts/cli/{version}/` where `{version}` is semver from the `.csproj` `<Version>` property.
- [ ] Each binary follows naming schema: `shadowdrop-cli-{version}-{rid}[.exe]`. Examples: `shadowdrop-cli-1.0.0-linux-x64`, `shadowdrop-cli-1.0.0-osx-arm64`, `shadowdrop-cli-1.0.0-win-x64.exe`.
- [ ] A `CHECKSUMS.sha256` file (Unix-standard format: `<hash>  <filename>`) is generated in the same directory listing all six binaries.
- [ ] A dedicated NUKE Publish target builds all six target executables in a single invocation.
- [ ] Each binary is executable and responds to `--help` and `--version` commands with exit code 0 (smoke test).
- [ ] GitHub Actions workflow includes a matrix job that builds and validates all six RIDs: native runners for `linux-x64` (linux), `osx-x64` (macOS), and `osx-arm64` (macOS); cross-compile on linux runner for `linux-arm64`, `win-x64`, `win-arm64`.
- [ ] Smoke tests run on accessible targets in CI: `linux-x64`, `osx-x64`, `osx-arm64`. Build-only validation on cross-compiled targets: `linux-arm64`, `win-x64`, `win-arm64`.

## Technical Details

Use the existing `ShadowDrop.Cli` Native AOT configuration as the baseline and extend validation to the full target
matrix. Keep the CLI implementation and dependencies trimming-safe; if a publish failure reveals an
AOT-hostile pattern, fix the code or dependency usage rather than immediately downgrading the distribution model.

### Artifact Contract & Release Layout

The publish process generates release-ready CLI artifacts in `artifacts/cli/{version}/` where `{version}` is the semver from the `.csproj` `<Version>` property. Each artifact is a native AOT standalone executable named following the schema `shadowdrop-cli-{version}-{rid}`, with platform-specific extensions (`.exe` on Windows; no extension on Unix/macOS).

All six Runtime Identifiers are published in a single batch: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`. Each binary is a complete, self-contained executable with no runtime dependencies.

A `CHECKSUMS.sha256` file (Unix-standard format: `<hash>  <filename>`) is generated in the same directory, listing the SHA-256 hash and filename for each binary. Users can verify integrity with standard `sha256sum -c CHECKSUMS.sha256` commands on Unix or `Get-FileHash` on Windows.

The publish workflow is implemented as a dedicated NUKE Publish target and is repeatable both locally and in CI, using GitHub Actions matrix with native runners for macOS and standard runners for Linux. All compiler warnings and AOT analysis output is captured in CI logs for blocker diagnosis.

### Validation Strategy (MVP)

**Smoke tests in CI** exercise initialization paths on accessible targets (linux-x64, osx-x64, osx-arm64) by validating that each binary is executable, responds to `--help` and `--version`, and exits with code 0. Cross-compiled targets (linux-arm64, win-x64, win-arm64) are build-validated only; AOT failures are nearly always compile-time, not runtime.
