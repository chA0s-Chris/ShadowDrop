## Rationale

Issue [#110](https://github.com/chA0s-Chris/ShadowDrop/issues/110) completes the release automation that was intentionally left out of #71. The current `release.yml` workflow validates a version, builds the release artifact bundle, and builds a multi-platform Docker image locally on the runner, but it does not publish the image or create a GitHub release. A real release should use the same version input as the source of truth for Docker tags, GitHub release metadata, and CLI artifacts.

## Acceptance Criteria

- [ ] `release.yml` logs in to Docker Hub using repository secrets.
- [ ] The Docker image is pushed to Docker Hub as `chaos/shadowdrop:x.x.x` for every release.
- [ ] Pre-release versions such as `x.x.x-xxx` are pushed with their full pre-release tag, for example `chaos/shadowdrop:x.x.x-xxx`.
- [ ] Non-pre-release versions are additionally tagged and pushed as `chaos/shadowdrop:latest`.
- [ ] Non-pre-release versions are additionally tagged and pushed as `chaos/shadowdrop:x.x`.
- [ ] Non-pre-release versions are additionally tagged and pushed as `chaos/shadowdrop:x`.
- [ ] The Docker image passes the existing multi-platform smoke test before it is pushed.
- [ ] The Docker Hub push does not run unless the multi-platform build and smoke test in the same job have succeeded.
- [ ] The workflow creates a GitHub release for the requested version.
- [ ] The workflow creates the git tag `vx.x.x` for the requested version, pointing at the commit the workflow was dispatched for.
- [ ] The GitHub release changelog is generated using Release Drafter.
- [ ] The GitHub release includes the six CLI binaries: Linux x64, Linux arm64, Windows x64, Windows arm64, macOS x64, and macOS arm64.
- [ ] The GitHub release includes the generated `CHECKSUMS.sha256` file.
- [ ] The GitHub release includes a link to the Docker Hub image.
- [ ] The workflow keeps the existing version validation behavior so invalid SemVer input fails before publishing.
- [ ] Publish steps do not run unless the release artifacts and Docker image build have succeeded.
- [ ] Automated tests or workflow validation checks cover the release tag calculation and pre-release detection.

## Technical Details

Extend `.github/workflows/release.yml` after the existing artifact and Docker image build steps. Keep the existing `version` input and validation job as the release source of truth. Because a Docker image built in one job is not available to later jobs, the Docker Hub login, retagging, and push run inside the existing `build-docker-image` job, after the multi-platform build has succeeded. The GitHub release job is the only new downstream job and depends on `build-docker-image` (and transitively on `release-artifacts`), so a failed artifact build or Docker build still prevents any external release side effects.

The Docker publish path authenticates to Docker Hub with the existing repository secrets `DOCKERHUB_USER` and `DOCKERHUB_PAT`, which are already configured and used by `ci.yml` — reuse them rather than introducing new secret names. The local build produces `shadowdrop:x.x.x`; since the job already enables the containerd image store, retagging the loaded multi-platform image to `chaos/shadowdrop` and pushing preserves the manifest list, so no rebuild is needed. Gate the push on the existing `SmokeTestDockerImageMultiPlatform` target so the image is only published after it boots and serves the health endpoint on both platforms. Because that target already `DependsOn` `BuildDockerImageMultiPlatform`, invoke it as the single build-and-smoke-test step (`build.sh SmokeTestDockerImageMultiPlatform`) instead of a separate build step, so the multi-platform image is built, loaded, and validated once without a redundant rebuild; log in and push the retagged image only after it passes. For every
version, publish the exact version tag. When the version contains a SemVer pre-release suffix, do not publish `latest`, minor, or major floating tags. When the version has no pre-release suffix, also publish `latest`, `MAJOR.MINOR`, and `MAJOR`. Keep the tag calculation in
a small, auditable script checked into the repository so it can be validated independently.

Create the GitHub release only after the Docker image has been pushed successfully, in a job that grants the `GITHUB_TOKEN` `contents: write` and `pull-requests: read` permissions. Use `release-drafter/release-drafter@v7` rather than hand-building release notes: pass `config-name: release-drafter.yml`, `version`, `name: vx.x.x`, `tag: vx.x.x`, `prerelease` determined by checking the `version` workflow input for a SemVer pre-release suffix, and `publish: false`. This retargets the rolling `vNext` draft maintained by `update-draft-release.yml` to the requested version while keeping it a draft. Pass `commitish` so the `vx.x.x` tag points at the commit the workflow was dispatched for, matching the built artifacts. Add the Docker Hub reference (`https://hub.docker.com/r/chaos/shadowdrop` plus the exact `chaos/shadowdrop:x.x.x` tag) via the action's `footer` input rather than the shared template in `.github/release-drafter.yml`, so the rolling vNext draft never carries the footer. The
drafter config sets `include-pre-releases: false`,
so changes shipped in pre-releases intentionally reappear in the next stable release's changelog. Release Drafter does not attach assets, so upload the six CLI files collected in `artifacts/release/cli/` plus `CHECKSUMS.sha256` to the draft with `gh release upload`, and only then publish it with `gh release edit --draft=false`, marking non-pre-release versions as the latest release. Publishing last keeps the release atomic: watchers are only notified once the binaries are attached, and a failed upload leaves a draft behind instead of an incomplete published release.

The existing `release-artifacts.yml` bundle already collects the API output, six CLI binaries, and `CHECKSUMS.sha256` into `shadowdrop-release-artifacts`. Reuse that bundle in `release.yml` for both Docker build input and release uploads rather than rebuilding artifacts in the publishing job.

Keep the tag and pre-release derivation in a small bash script checked into the repository (for example `scripts/calculate-docker-tags.sh`) that takes the version and prints the resulting tag set and a pre-release flag. Verify it with a plain bash test script that asserts the expected output for representative versions (stable, pre-release, multi-digit); do not introduce a new test framework. Run the test script and `actionlint` as steps in `ci.yml` so pull requests touching the script or the workflows are validated before release day.
