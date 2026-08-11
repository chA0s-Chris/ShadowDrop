# Publish a CLI Docker image (multi-arch, chiseled)

> Issue: [#163](https://github.com/chA0s-Chris/ShadowDrop/issues/163)

## Rationale

The CLI ships only as a downloadable release binary. Locked-down workstations, hosts with no writable directory on `PATH`, policies against `curl | sh`, and ephemeral CI jobs can often run a container but cannot install a binary. Publishing `chaos/shadowdrop-cli` as a versioned multi-arch image alongside the existing `chaos/shadowdrop` server image gives those users a supported path on the same release cadence.

## Acceptance Criteria

- [ ] A new `Dockerfile.cli` builds an image from the patch-pinned `runtime-deps:<x.y.z>-noble-chiseled` tag, matching how `Dockerfile` pins `aspnet`, that runs as the base image's non-root user and entrypoints `/usr/local/bin/shadowdrop`, so `docker run … chaos/shadowdrop-cli upload …` needs no command name.
- [ ] The image sets `HOME=/home/app` so config resolution stays deterministic when the caller overrides the user, and `SHADOWDROP_NO_UPDATE_CHECK=1` so the CLI never prints installer guidance that does not apply inside a container.
- [ ] The image's working directory is writable by the runtime user, so `download` without `--out` lands in a bind-mounted host directory, and an unmounted run under the image's default user succeeds rather than failing on permissions.
- [ ] Building for `linux/amd64` and `linux/arm64` produces one tag backed by a manifest list, each platform carrying the natively matching CLI binary.
- [ ] Building the CLI image without the Linux CLI release binaries present fails with an actionable message naming the target that produces them, rather than producing an incomplete or wrong-architecture image.
- [ ] A multi-platform smoke test runs the image once per platform and fails unless `shadowdrop --version` exits zero and reports `ShadowDrop v<release-version>`.
- [ ] `scripts/calculate-docker-tags.sh` derives `source_image` from the requested Docker repository so the server and CLI images can coexist in the local image store during a release; its output for the default repository is unchanged.
- [ ] `scripts/test-calculate-docker-tags.sh` covers `source_image` derivation for the CLI repository across stable, prerelease, and non-floating-tag cases.
- [ ] A release run publishes the CLI image to `chaos/shadowdrop-cli` under the same version, floating-tag, and prerelease rules that already govern `chaos/shadowdrop`.
- [ ] The drafted GitHub release footer links both the API server image and the CLI image at the released version.
- [ ] The footer renders as its own block instead of being absorbed into the last changelog list item, and the `vNext` draft produced by `update-draft-release.yml` gains no stray separator.
- [ ] `docs/CLI.md` documents container usage, covering mounted-volume file ownership, that `--interactive` requires `docker run -it`, and that updates come from pulling a new tag.
- [ ] `README.md` presents the CLI image as a distribution channel alongside the release binaries.

## Technical Details

### Base image

`runtime-deps` rather than the API's `aspnet` base: the CLI is self-contained Native AOT and needs no .NET runtime, only a matching libc and loader, the OpenSSL 3 libraries it `dlopen`s, and CA certificates — all present in the chiseled `runtime-deps` image, which also has no shell and no package manager. Pin the patch version as `Dockerfile` already does for `aspnet`, so Renovate's Dockerfile manager tracks it.

Chiseled is deliberately chosen over Alpine even though musl binaries now exist (#162): the glibc `linux-x64`/`linux-arm64` artifacts are already built and released, and the shell-free base matches `DEPLOYMENT_HARDENING.md`. The cost is that a user hitting a volume-permission problem cannot inspect the container; that is handled in the documentation rather than by shipping a second image.

`HOME` is set but the directory keeps the base image's permissions. Nothing under `$HOME` is written: `CliConfigurationResolver` only reads the file `CliConfigPathResolver` returns, and `SHADOWDROP_NO_UPDATE_CHECK=1` suppresses the only writer, `UpdateCheckCachePathResolver`. Mounting a config file over the path needs no write access either.

The working directory is a different matter and must be set explicitly. The base image declares none, leaving `/`, which the runtime user cannot write, while `download` without `--out` writes to `./<original-filename>`; the plainest container download would therefore fail. Use `/data` — matching the mount path the issue's motivating example already uses — and ship it as an owned, writable directory with `COPY --chown --chmod` of an empty staging directory, the way `Dockerfile` ships `docker/app-data/`. Shipping it rather than only declaring `WORKDIR` is what keeps an unmounted run working for the image's default user, since Docker would otherwise create `/data` as `root:root` at container start and reproduce the same permission failure under a better name.

### Architecture-specific artifact staging

`BuildDockerImageMultiPlatform` works today because the API publish output is architecture-neutral IL, so identical files are copied onto every platform base. That does not hold here. A new pipeline step stages the two glibc release binaries into fixed, arch-named paths before the build:

```
artifacts/publish/cli/<version>/shadowdrop-<version>-linux-x64   -> artifacts/docker/cli/amd64/shadowdrop
artifacts/publish/cli/<version>/shadowdrop-<version>-linux-arm64 -> artifacts/docker/cli/arm64/shadowdrop
```

so `Dockerfile.cli` selects with `COPY --chmod=755 artifacts/docker/cli/${TARGETARCH}/shadowdrop /usr/local/bin/shadowdrop` under `ARG TARGETARCH`. This keeps both the version and the `amd64`→`x64` mapping out of the Dockerfile, and lets one `docker buildx build --platform` invocation cover both platforms — a build-arg-per-platform approach cannot, because buildx does not vary build args across platforms in a single invocation.

Source paths come from `PublishCliDirectory / SemanticVersion` and `GetCliArtifactName`, so the staging step reuses the existing naming rather than duplicating it.

Both `COPY` sources have to be reachable from the build context, and neither is today: `.dockerignore` is an allow-list — `**` followed by explicit re-includes for the API publish output and `docker/app-data/` — so the staged binaries and the working-directory staging directory are excluded. Give the CLI image its own `Dockerfile.cli.dockerignore`, which BuildKit prefers over `.dockerignore` for that build, so the two images' contexts stay independent instead of each accumulating re-includes for the other's inputs.

### Build pipeline

Add to `build/BuildPipeline.Publish.cs`, alongside the existing API image targets: a CLI image repository constant (`shadowdrop-cli`, matching the derived `source_image`), the staging step, `BuildCliDockerImageMultiPlatform`, and `SmokeTestCliDockerImageMultiPlatform`. Reuse `BuildDockerImageCore`'s buildx invocation and its containerd-image-store diagnostic by parameterizing the Dockerfile path and tag rather than duplicating the logic.

Unlike `EnsurePublishApiArtifacts`, the CLI staging step must not republish as a local-dev fallback — cross-compiling `linux-arm64` needs `gcc-aarch64-linux-gnu`, which local machines generally lack. It asserts both binaries exist and names `PublishCliLinux` in the failure message.

The smoke test is cheaper than the API's: no container to wait on becoming healthy, just `docker run --rm --platform <p> <tag> --version` per platform. Asserting the exact `ShadowDrop v<version>` string (`CliVersion` strips the `+<sha>` suffix from the informational version) catches both a wrong-architecture binary — the most likely failure of the `TARGETARCH` wiring — and a stale artifact bundle.

### Release plumbing

`scripts/calculate-docker-tags.sh` hardcodes `source_image=shadowdrop:${version}` while already accepting the repository as `$2`. Derive it from the repository's last path segment so `chaos/shadowdrop-cli` yields `shadowdrop-cli:${version}`; the default repository keeps producing `shadowdrop:${version}`, so the existing expectations in the test script stay valid.

Add a `build-cli-docker-image` job to `.github/workflows/release.yml` modeled on `build-docker-image`, substituting the CLI restore path and repository. The bundle's `artifacts/release/cli/` contents restore into `artifacts/publish/cli/<version>/`, which is where the staging step looks. The job depends on `release-artifacts` and runs in parallel with the server image job.

Pushing to `chaos/shadowdrop-cli` requires either that the repository already exists on Docker Hub or a `DOCKERHUB_PAT` that is not repository-scoped; Docker Hub auto-creates a public repository on first push only in the latter case, and a scoped token fails at the push step, after the image has been built and smoke-tested. Create the repository with the same visibility as `chaos/shadowdrop` before the first release that includes this job.

### Release notes footer

The `github-release` job needs the CLI repository's tag-script output as well as the server's, so it links both images at the released version. It currently declares `needs: build-docker-image` and must depend on both image jobs: otherwise a release can be drafted and published while the CLI image is still building, or after its push failed, leaving a published footer that links a tag which does not exist.

release-drafter concatenates its `footer` input directly onto the rendered `template`, which ends with the last changelog list item. Without an explicit blank line the footer becomes a lazy continuation of that item and renders inside it. The separator therefore belongs in the `footer` value in `release.yml`, **not** at the end of `template` in `.github/release-drafter.yml`: `update-draft-release.yml` reuses that config with no footer, so a separator in the shared template would leave a dangling artifact on the `vNext` draft.

A leading empty line in a YAML `|` block scalar is preserved, since the block's indentation is taken from the first non-empty line. There is no local check for the rendered result: confirm it on the drafted release body, which `github-release` produces with `publish: false` before a later step flips `--draft=false`. The spacer is easy to lose to a reformat, so it is worth a comment.

### Documentation

`docs/CLI.md` gains a container section covering the two things users will get wrong: downloads land in the mounted volume owned by UID 1654 unless `--user $(id -u):$(id -g)` is passed, and `--interactive` needs `docker run -it` or the Spectre prompts have no TTY. It should also show configuration via `SHADOWDROP_SERVER_URL`/`SHADOWDROP_UPLOAD_TOKEN`, note that the image has no shell, and state that updating means pulling a new tag.

`README.md` currently says ShadowDrop ships through two channels and describes the CLI as binaries only; that becomes inaccurate.

### Out of scope

A musl/Alpine variant. It only becomes interesting for layering the CLI into a user's own image via `COPY --from=…`, which requires a libc match with the target image. Follow-up if there is demand.
