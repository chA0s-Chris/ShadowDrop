# Ship musl CLI binaries for Linux

> Issue: [#162](https://github.com/chA0s-Chris/ShadowDrop/issues/162)

## Rationale

The dynamically linked glibc CLI binaries cannot execute on musl-based Linux distributions such as Alpine, despite containerized CI being a primary CLI use case. Add native musl release assets while retaining glibc as the default Linux option.

The change will use two stacked review layers: release production first, followed by installer selection, tests, and documentation.

## Acceptance Criteria

### Layer 1: Release Assets

- [ ] Releases include dynamically linked Native AOT binaries named `shadowdrop-<version>-linux-musl-x64` and `shadowdrop-<version>-linux-musl-arm64`, in addition to the existing six CLI binaries.
- [ ] The release-artifact workflow builds each musl binary natively in the .NET 10 Alpine SDK container, using x64 and arm64 GitHub-hosted runners respectively.
- [ ] Each musl binary is smoke-tested on its native architecture during publication, so an asset that cannot execute fails the build.
- [ ] Artifact collection requires exactly eight CLI binaries and generates checksums for all of them.
- [ ] GitHub release publication requires the eight CLI binaries plus `CHECKSUMS.sha256`.

### Layer 2: Installer and Documentation

- [ ] On Linux, `install.sh` selects the matching glibc or musl asset for both x64 and arm64 while preserving existing macOS selection.
- [ ] `SHADOWDROP_INSTALLER_LIBC` provides deterministic glibc/musl selection for testing, applies to Linux selection only and is ignored on macOS, and fails unsupported override values with an actionable diagnostic before downloading release files.
- [ ] Installer tests cover automatic musl and glibc detection through a stubbed detection command, explicit libc overrides, both supported Linux architectures, and exact checksum-manifest matching without changing Windows selection.
- [ ] `README.md` and `docs/CLI.md` document the additional musl assets while retaining the glibc binaries as the primary Linux downloads.

## Technical Details

### Stack Design

1. `0162-ship-musl-cli-binaries-for-linux-release-assets` — add and validate musl CLI publication and release-asset collection.
2. `0162-ship-musl-cli-binaries-for-linux-installer-docs` — add installer selection, fixtures, tests, and documentation; depends on the release-asset naming established above.

Add `linux-musl-x64` and `linux-musl-arm64` to the CLI project’s runtime identifiers and introduce a dedicated `LinuxMuslCliRuntimeIdentifiers` allow-list with separate `PublishCliLinuxMuslX64` and `PublishCliLinuxMuslArm64` targets, so each workflow matrix leg publishes only its native RID and musl binaries are never cross-published. Use `mcr.microsoft.com/dotnet/sdk:10.0-alpine`, whose SDK must satisfy the `global.json` floor of 10.0.302, installing `bash`, `git`, `clang`, `lld`, `build-base`, and `zlib-dev`; run arm64 on `ubuntu-24.04-arm`. The musl arm64 build must not use the glibc cross-linker or `ObjCopyName` override.

Current-RID detection must become musl-aware. It derives the runtime identifier from the operating-system platform alone and returns `linux-x64` on Alpine, so the publish pipeline’s native-RID smoke test would silently skip every musl binary.

Layer 1 can be checked locally with the repository’s normal verification and a native x64 publish in the Alpine SDK container. Native arm64 publication is an explicit post-push verification boundary: dispatch `release-artifacts.yml` against the release-assets branch and require that run to succeed before considering the layer fully validated.

Do not enable `StaticExecutable`: .NET cryptography dynamically loads OpenSSL, so a fully static musl executable would fail during cryptographic and HTTPS operations. The new binaries remain dynamically linked, and the existing glibc assets remain unchanged.

On Linux, determine libc before assembling the RID. An explicit `SHADOWDROP_INSTALLER_LIBC` value takes precedence; otherwise detect musl by inspecting `ldd --version` output for a musl signature and fall back to glibc whenever `ldd` is absent or does not positively report musl. Keeping detection in a `PATH`-resolved command lets the Bats suite stub it the way it already stubs `curl` and the checksum tools, so automatic selection is covered on the glibc CI runners. Because stubbed tests cannot validate the signal itself, confirm automatic detection once on a real musl system by running `install.sh` inside an Alpine container against a release carrying the musl assets. Detection must read the command’s combined output and ignore its exit status: musl’s `ldd` reports its banner on stderr and exits nonzero, so a stdout-only or exit-status-gated check would silently fall back to glibc. Keep the existing exact suffix matching because `-linux-musl-x64` and `-linux-x64` remain unambiguous.
Extend the Bats release fixtures with both musl assets and the Pester checksum fixture so Windows tests exercise a complete eight-binary manifest.
