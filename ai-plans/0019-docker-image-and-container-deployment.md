## Rationale

Package the API for self-hosted single-container deployment on home lab and small VPS hosts. The image uses filesystem-local blob storage and LiteDB persistence with predictable writable paths, environment-driven configuration, and non-root execution for security.

## Acceptance Criteria

- [ ] Dockerfile uses multi-stage build; final stage bases on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`.
- [ ] Container runs with non-root user (uid/gid `1000:1000`); `/app/data` and all contents owned by that user.
- [ ] All acceptance criteria below are satisfied on both `amd64` and `arm64` architectures.
- [ ] Container runtime defaults: HTTP server binds to `0.0.0.0:19423` by default (single port exposed in Dockerfile; both public downloads and protected admin routes served together on this port unless `ASPNETCORE_URLS` is overridden).
- [ ] All persistent data is writable to `/app/data` (LiteDB at `/app/data/shadowdrop.db` by default; blobs at `/app/data/blobs/`); `/app/data` **must be mounted as a volume** for data persistence beyond container lifetime.
- [ ] Directory permissions under `/app/data` are `700`; database file is `600`.
- [ ] Configuration via environment variables (e.g., `Serilog__MinimumLevel__Default=Debug` for verbosity; `ShadowDrop:LiteDbPath` for LiteDB path).
- [ ] Users may mount a custom `appsettings.json` over `/app/appsettings.json` in the image.
- [ ] Container does not require HTTPS; assume reverse-proxy TLS termination at ingress.
- [ ] No liveness probe or health check endpoint in MVP.
- [ ] Containerized smoke test validates: container starts without errors, loads configuration correctly (verified via logs or startup success), and responds with HTTP 200 to `GET /health` proving API is ready to accept requests (no external dependencies).
- [ ] On first start, if `/app/data/shadowdrop.db` does not exist, LiteDB creates the database and schema automatically; subsequent starts reuse the existing database.
- [ ] Multi-arch build validation: `docker buildx build --platform linux/amd64,linux/arm64 -t shadowdrop:latest .` builds successfully on both architectures without errors.

## Technical Details

Create the Docker packaging around the existing ASP.NET Core API without adding container-specific behavior into feature slices. Use a multi-stage build that:

- Builds on a .NET SDK base in stage 1
- Publishes the release binary in stage 2
- Copies only runtime assets into the final `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` image in stage 3

The final image must:

- Create a non-root user (uid/gid `1000:1000`)
- Set `/app/data` as writable with `700` permissions; database file as `600`
- Expose port `19423` by default
- Set environment defaults to point LiteDB to `/app/data/shadowdrop.db` and blob storage to `/app/data/blobs/`
- Disable HTTPS (reverse proxy handles TLS at ingress)
- Support both `amd64` and `arm64` architectures

Configuration remains environment-variable-driven (e.g., `ASPNETCORE_*`, `Serilog:*`, `ShadowDrop:*` sections bound via `appsettings.json` and environment overrides). Use existing Serilog configuration for debug verbosity. The bootstrap admin token (`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`) must come from environment and not be baked into the image. Users may override the provided `appsettings.json` by mounting a custom file.

Database initialization: LiteDB automatically creates `/app/data/shadowdrop.db` and initializes schema on first run if the file does not exist. The application startup logic already handles this; document it in the smoke test and deployment guide so users understand data persistence is automatic upon mount.

Smoke test validation: Use `docker buildx build --platform linux/amd64,linux/arm64 -t shadowdrop:latest .` to build and validate both architectures. For single-container smoke test, start the container, verify startup logs, and confirm HTTP 200 response to `GET /health`.

Update `README.md` with one-container deployment guidance including:

- Docker run command example (port mapping, volume mounts for persistence)
- Docker Compose example (single container with mounted volumes)
- Environment variable reference (LiteDB path, blob storage root, log level, bootstrap token)
- Note that HTTPS is handled by a reverse proxy (e.g., nginx, Caddy) in front of the container
- First-start database creation behavior (automatic upon mount and startup)
