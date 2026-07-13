# Add Docker Compose deployment options

> Issue: [#138](https://github.com/chA0s-Chris/ShadowDrop/issues/138)

## Rationale

Provide reproducible, single-host Docker Compose deployments for ShadowDrop's two recommended persistence models: the default LiteDB metadata with filesystem blobs, and MongoDB metadata with GridFS blobs. Keep each option explicit and simple to start while preserving secure defaults and documenting the operational boundaries of both configurations.

## Acceptance Criteria

- [ ] Add a separate Compose file for the default LiteDB metadata and filesystem blob configuration.
- [ ] Persist all LiteDB and filesystem state in a named volume mounted at `/app/data`.
- [ ] Add a separate Compose file for ShadowDrop with one MongoDB service.
- [ ] Configure the MongoDB variant to use `MongoDb` metadata and `MongoGridFs` blob storage.
- [ ] Persist MongoDB data in its own named volume.
- [ ] Replace `GET /health` with distinct `GET /health/live` and `GET /health/ready` endpoints; liveness reports process availability, while readiness performs a bounded MongoDB check whenever a MongoDB provider is active and returns HTTP 503 when that dependency is unavailable.
- [ ] Add a shell-free API health-probe executable to the container image and use it to probe API readiness from both Compose files.
- [ ] Add a credential-aware MongoDB health check and gate API startup on MongoDB health in the MongoDB variant.
- [ ] Pin MongoDB to the `mongo:8.3` image tag (latest 8.3.x patch release) instead of `latest`.
- [ ] Commit a `.env.example` that names every required setting and secret without providing real or usable default credentials, accept the MongoDB connection string as a complete operator-supplied value, and ignore the credential-bearing `.env` file while retaining `.env.example` in version control.
- [ ] Bind the API to `127.0.0.1:19423` by default and document the explicit change required for LAN access.
- [ ] Document startup commands for both Compose variants while retaining the existing `docker run` guidance.
- [ ] Document reverse-proxy TLS termination and require upstream protection when exposing `/api/admin/*`.
- [ ] Describe the MongoDB and GridFS variant as a convenient single-host deployment rather than a highly available MongoDB topology.
- [ ] Document coordinated backup and restore considerations for `/app/data`, MongoDB metadata, both GridFS collections, and the distributed-lock collection.
- [ ] Update the README quick start and deployment guide to reference both Compose options and remove the obsolete statement that the Docker Hub image has not yet been published.
- [ ] Validate both Compose files with `docker compose config` using documented non-secret test configuration.
- [ ] Smoke-test both variants and confirm that each API reaches its expected healthy state.
- [ ] Confirm that the MongoDB variant does not start the API until MongoDB is healthy.
- [ ] Add an opt-in `SmokeTestDockerCompose` target that uses each variant to create representative metadata and encrypted blob data, recreates services without deleting volumes, and verifies that the original admin credential and persisted data remain usable.
- [ ] Keep the full behavioral Compose persistence target outside normal pull-request CI while running `docker compose config` for both files in regular CI.
- [ ] Add focused automated tests for liveness, local readiness, MongoDB-backed readiness success and failure through controllable test doubles, and health-probe success, unhealthy-response, connection-failure, timeout, and exit-code behavior.
- [ ] Add one MongoDB readiness happy-path assertion to the existing MongoDB integration fixture without introducing another MongoDB container lifecycle.

## Technical Details

Add two clearly named Compose files at the repository root rather than combining the persistence choices behind profiles. Both should run `chaos/shadowdrop:latest`, consume operator-supplied values from the committed `.env.example` contract, publish container port `19423` only on loopback by default, and use an exec-form, shell-free probe command against API readiness. The local variant should rely on the image's LiteDB and filesystem defaults and attach a named volume at `/app/data`, matching the paths already established by the `Dockerfile`.

The MongoDB variant should add one MongoDB service pinned to the `mongo:8.3` tag so it tracks the latest 8.3.x patch release, persist `/data/db` in a dedicated named volume, and configure authentication without embedding credentials in version-controlled files. Pass `ShadowDrop__Metadata__Provider=MongoDb`, `ShadowDrop__Storage__Provider=MongoGridFs`, and the existing `ShadowDrop__Mongo__*` settings to the API. The environment contract should name `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`, `MONGO_INITDB_ROOT_USERNAME`, `MONGO_INITDB_ROOT_PASSWORD`, and `SHADOWDROP_MONGO_CONNECTION_STRING`; Compose should map the complete operator-supplied connection string to `ShadowDrop__Mongo__ConnectionString` rather than reconstructing a URI from credential fragments. Add `.env` to `.gitignore` while keeping `.env.example` committed. Give MongoDB a credential-aware health check and use Compose's health-based dependency condition so ShadowDrop starts only after MongoDB is ready. Keep both service
definitions compatible with the
image's non-root runtime and existing bootstrap-admin-token behavior.

Extend `HealthEndpoints` to replace the pre-1.0 `GET /health` contract with `GET /health/live` and `GET /health/ready`; no compatibility alias is required. Liveness should report whether the process is serving requests. Readiness should succeed after normal local-persistence startup and, whenever `ShadowDropOptions.RequiresMongo` is true, execute a cancellation-aware MongoDB ping with a short bounded timeout and return HTTP 503 on failure. Register the readiness dependency through the composition root so the endpoint remains independent of provider-specific details. Test readiness failure through a controllable manual test double rather than repeatedly stopping real MongoDB containers, and add one happy-path assertion to the existing MongoDB integration fixture so it reuses that fixture's running server.

Add a framework-dependent `ShadowDrop.HealthProbe` console project containing architecture-neutral IL and include it in `ShadowDrop.slnx`. Publish it through `BuildPipeline.Publish` alongside the API, copy its output into the chiseled image from `Dockerfile`, and invoke it with an exec-form `dotnet` command so no shell or external HTTP utility is required. Both API service health checks should point the probe at `/health/ready`. Update `BuildPipeline.Publish`'s existing Docker smoke test to use `/health/ready`, and migrate every existing `/health` caller in the API tests, MongoDB integration tests, and end-to-end test infrastructure (including `ApiServerProcess`) to `/health/live` or `/health/ready` according to the behavior under test. Cover successful probes, unhealthy HTTP responses, connection failures, bounded timeouts, and process exit codes with focused automated tests.

Extend `README.md` and `docs/DEPLOYMENT.md` with copyable startup commands, environment preparation, loopback-versus-LAN binding guidance, the new liveness/readiness contracts, and a concise comparison of the two files. Preserve the current `docker run` examples as a supported alternative. Remove the stale deployment-guide note that release publishing is not wired up; `chaos/shadowdrop` is available from Docker Hub and is the supported Compose image. Reiterate that containers serve plain HTTP and require a TLS-terminating reverse proxy, and that `/api/admin/*` needs upstream exposure controls. Describe MongoDB plus GridFS as a convenient single-host configuration with no high-availability guarantees. Expand backup and restore guidance so the local named volume is captured as one unit and MongoDB backups consistently include ShadowDrop metadata collections, both `<bucket>.files` and `<bucket>.chunks`, and the Chaos.Mongo distributed-lock collection.

Validate each rendered configuration without exposing substituted secrets, and run those inexpensive `docker compose config` checks in normal pull-request CI. Add an opt-in `SmokeTestDockerCompose` build target for the behavioral persistence matrix instead of adding two full deployment lifecycles to every pull request. The target should build the current branch image, generate a temporary Compose override under ignored build artifacts that substitutes only the API image with the local tag, and exercise each committed production Compose file through that override; the operator-facing files must remain fixed to `chaos/shadowdrop:latest`.

For each variant, the smoke target should start from uniquely named clean volumes, wait for API readiness, create a small upload and share through the normal CLI/API workflow, recreate the services without deleting volumes, and verify that the original admin credential, metadata, and encrypted blob remain usable. It should also confirm MongoDB health-gated API startup and the selected providers. Keep the target out of the normal `Test` and `TestEndToEnd` dependency graph, but execute it during implementation and document it for repeatable pre-release validation. Use finally-style cleanup for its temporary override, containers, networks, and uniquely named volumes so failure cannot endanger unrelated Docker resources. Automated endpoint and probe tests should complement rather than replace this deployment-level workflow.
