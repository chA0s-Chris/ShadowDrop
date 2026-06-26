## Rationale

Package the API for self-hosted single-container deployment on home lab and small VPS hosts. The image uses filesystem-local blob storage and LiteDB persistence with predictable writable paths, environment-driven configuration, and non-root execution for security. The `PublishApi` NUKE target from [#69](https://github.com/chA0s-Chris/ShadowDrop/issues/69) already produces the framework-dependent API publish output under `artifacts/publish/api/`, so the Dockerfile only needs to copy that output into a runtime image rather than building the API itself.

A `BuildDockerImage` NUKE target builds a single-arch image for the **host's native platform** to keep the local dev/test inner loop fast and dependency-free (`./build.sh PublishApi BuildDockerImage`), and a `SmokeTestDockerImage` target runs that image and checks `GET /health`. The release-grade multi-platform (`linux/amd64` + `linux/arm64`) image build and the release workflow that produces it are split out into [#71](https://github.com/chA0s-Chris/ShadowDrop/issues/71), which builds on the Dockerfile and shared helper introduced here.

## Acceptance Criteria

- [ ] Dockerfile uses a single runtime stage based on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` that copies the pre-built API publish output from `artifacts/publish/api/` (produced by the `PublishApi` NUKE target); no .NET SDK build happens inside the image.
- [ ] Container runs as the chiseled base image's built-in non-root user (`USER $APP_UID`, uid `1654`); no custom user is created, and `/app/data` and all contents are owned by / writable to that user.
- [ ] The Dockerfile is architecture-neutral by construction (framework-dependent IL on a multi-arch base), so it is designed to run on both `amd64` and `arm64`; it is built and validated for the host's native architecture here, with full multi-architecture build/runtime validation handled in #71.
- [ ] Container runtime defaults: the Dockerfile sets `ASPNETCORE_HTTP_PORTS=19423` (overriding the chiseled base's default of `8080`) and `EXPOSE 19423`, so the HTTP server binds to `0.0.0.0:19423` by default (single port; both public downloads and protected admin routes served together unless `ASPNETCORE_URLS`/`ASPNETCORE_HTTP_PORTS` is overridden).
- [ ] All persistent data is writable under `/app/data`: LiteDB at `/app/data/metadata/shadowdrop.db` (via `ShadowDrop__Metadata__LiteDbPath`) and blob storage at `/app/data/storage/` (via `ShadowDrop__Storage__LocalRoot`); `/app/data` **must be mounted as a volume** for data persistence beyond container lifetime.
- [ ] Directory permissions under `/app/data` are `700`; database file is `600`.
- [ ] Configuration via environment variables using the `__` delimiter (e.g., `Serilog__MinimumLevel__Default=Debug` for verbosity; `ShadowDrop__Metadata__LiteDbPath` for the LiteDB path; `ShadowDrop__Storage__LocalRoot` for the blob storage root).
- [ ] Users may mount a custom `appsettings.json` over `/app/appsettings.json` in the image.
- [ ] Container does not require HTTPS; assume reverse-proxy TLS termination at ingress.
- [ ] No Docker `HEALTHCHECK` instruction or container liveness probe in MVP (the existing `GET /health` endpoint remains and is used by the smoke test).
- [ ] Containerized smoke test validates: container starts and reaches the listening state with no `Fatal`/`Error` entries in the startup logs (proving configuration loaded successfully), and responds with HTTP 200 to `GET /health` proving the API is ready to accept requests (no external dependencies).
- [ ] A `SmokeTestDockerImage` NUKE target automates the smoke test against the native image: it `.DependsOn(BuildDockerImage)` to build the image, starts the container, polls `GET /health` until it returns HTTP 200 (with a bounded timeout), and tears the container down afterward, failing the build if `/health` never becomes healthy. Because of the dependency, `./build.sh SmokeTestDockerImage` builds the image and runs the smoke test in one invocation, both locally and from CI.
- [ ] On first start, if `/app/data/metadata/shadowdrop.db` does not exist, LiteDB creates the database and schema automatically; subsequent starts reuse the existing database.
- [ ] A `BuildDockerImage` NUKE target builds a single-arch image for the **host's native platform** and loads it into the local image store (`docker buildx build --load`, no `--platform` override). It works on any standard Docker daemon — it does **not** require the containerd image store or QEMU — so it is the fast, low-friction option for local development and the smoke test.
- [ ] `BuildDockerImage` builds from the API publish output in `artifacts/publish/api/`, derives the image tag from `SemanticVersion`, and is implemented via a shared private helper (Dockerfile, artifact check, tagging) designed so the multi-platform target in #71 can reuse it. It does **not** hard-depend on `PublishApi` (no `DependsOn`) but is ordered `After(PublishApi)`, so `./build.sh PublishApi BuildDockerImage` builds the artifacts and then the image in one run, while `./build.sh BuildDockerImage` builds against pre-existing artifacts (e.g. artifacts downloaded in CI). It fails with a clear error if the expected API publish output is missing.

## Technical Details

Create the Docker packaging around the existing ASP.NET Core API without adding container-specific behavior into feature slices. The `PublishApi` NUKE target already publishes the API (framework-dependent, Release configuration) to `artifacts/publish/api/`, so the Dockerfile does not need an SDK build stage. Instead, use a single-stage Dockerfile that:

- Bases directly on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`
- Copies the contents of `artifacts/publish/api/` into the image (e.g. `/app`)
- Sets the entrypoint to run the published API assembly

Because the publish output is framework-dependent IL, a single set of artifacts is architecture-neutral and works on both the `amd64` and `arm64` variants of the runtime base image. The CI/release flow must run `PublishApi` before invoking the Docker build so the build context contains the publish output; the Dockerfile assumes these artifacts are present rather than producing them.

The final image must:

- Run as the chiseled base image's built-in non-root user via `USER $APP_UID` (uid `1654`). Do **not** create a custom user — the chiseled image has no shell, so `RUN useradd` is unavailable.
- Ensure `/app/data` is writable by the runtime user, with `700` directory permissions and `600` on the database file. Because the chiseled image has no shell (`RUN chmod`/`chown` are unavailable), apply ownership/mode via `COPY --chown`/`--chmod` for any baked-in paths and have the application create and `chmod` its data directories/files at runtime on first start (the API already writes blob files with `600`).
- Set `ASPNETCORE_HTTP_PORTS=19423` (overriding the base image's `8080` default) and `EXPOSE 19423`
- Set environment defaults `ShadowDrop__Metadata__LiteDbPath=/app/data/metadata/shadowdrop.db` and `ShadowDrop__Storage__LocalRoot=/app/data/storage/`
- Disable HTTPS (reverse proxy handles TLS at ingress)
- Remain architecture-neutral (framework-dependent IL) so the same image definition serves both `amd64` and `arm64`; producing the multi-platform image is handled in #71

Configuration remains environment-variable-driven, using the `__` delimiter for nested keys (e.g., `ASPNETCORE_*`, `Serilog__*`, `ShadowDrop__*` overrides layered over `appsettings.json`). Use existing Serilog configuration for debug verbosity. The bootstrap admin token (`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`) must come from environment and not be baked into the image. Users may override the provided `appsettings.json` by mounting a custom file.

Database initialization: LiteDB automatically creates `/app/data/metadata/shadowdrop.db` and initializes schema on first run if the file does not exist. The application startup logic already handles this; document it in the smoke test so it is clear that data persistence is automatic upon mount.

Smoke test validation: Automate the check with a `SmokeTestDockerImage` NUKE target (added to `build/BuildPipeline.Publish.cs` alongside the image target and wired `.DependsOn(BuildDockerImage)`). The hard dependency ensures the native single-arch image is built (fast, no containerd/QEMU prerequisites) before the smoke test runs, so the target works whether invoked standalone (`./build.sh SmokeTestDockerImage`) or as part of a larger chain. It starts the container, polls `GET /health` until it returns HTTP 200 within a bounded timeout (also checking startup logs for `Fatal`/`Error` entries), and tears the container down in a `finally`-style cleanup so a failed run does not leak containers; the target fails the build if `/health` never becomes healthy. It is runnable locally and from CI. Full multi-architecture build validation is handled in #71 via `BuildDockerImageMultiPlatform`.

Docker image NUKE target: Add `BuildDockerImage` to the publish-related partial build file `build/BuildPipeline.Publish.cs`. Factor the common work into a single private helper that takes the target platform(s) (and optionally a `--load` flag) — designed so the multi-platform target in #71 can reuse it — reusing the existing `PublishApiDirectory` helper from `build/BuildPipeline.Common.cs` to locate the artifacts and `SemanticVersion` for the image tag. The image tag derives from `SemanticVersion`, which `OnBuildCreated` parses and validates from the existing `ReleaseVersion` parameter (CLI `--release-version`); no new version plumbing is introduced. The helper wraps a `docker buildx build` invocation (Nuke's `Nuke.Common.Tools.Docker` tooling or a direct process call) pointed at the repository Dockerfile, with the build context arranged so the Dockerfile's `COPY` of `artifacts/publish/api/` resolves. Before invoking `docker`, it asserts that `PublishApiDirectory` exists and is
non-empty, failing fast with a clear message otherwise.

`BuildDockerImage` builds for the **host's native platform** (`docker buildx build --load` with no `--platform` override) and loads it into the local image store. It works on any standard Docker daemon — no containerd image store or QEMU required — making it the fast option for local development and the smoke test.

Preconditions: both `BuildDockerImage` and `SmokeTestDockerImage` require a Docker daemon with `buildx` available on the machine that runs them (local dev as well as the CI runner). No containerd image store or QEMU is needed for these native-arch targets; those prerequisites are introduced only by the multi-platform build in #71.

`BuildDockerImage` must use `.After(PublishApi)` rather than `.DependsOn(PublishApi)` so it does not trigger a republish; this lets it serve both the local chain `./build.sh PublishApi BuildDockerImage` and a flow that builds from pre-existing artifacts. Do not change `BuildPipeline.Main()`; the default `Build` entry point stays unchanged, and the existing `Publish` aggregate target is not required to depend on the Docker target.
