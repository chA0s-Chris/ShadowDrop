## Rationale

Package the API for the default self-hosted deployment model. This slice should produce a practical one-container Docker
distribution with filesystem storage and LiteDB persistence paths that fit home lab and small VPS use cases.

## Acceptance Criteria

- [ ] A Docker image can run `ShadowDrop.Api` as the default server distribution.
- [ ] The container image uses a production-ready multi-stage build.
- [ ] Runtime configuration supports LiteDB path, local storage root, API exposure settings, and
  `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`.
- [ ] The image exposes the API in a way that supports separate handling of public download routes and protected
  management routes through deployment configuration.
- [ ] The default container layout supports durable volumes for local blob storage and LiteDB metadata.
- [ ] Documentation in `README.md` is updated with one-container deployment guidance.
- [ ] A containerized smoke test proves the API starts successfully with the expected configuration shape.

## Technical Details

Create the Docker packaging around the existing ASP.NET Core API rather than adding container-specific behavior into
feature slices. Prefer a multi-stage build that restores, publishes, and then copies only the required runtime assets
into the final image.

The deployment model should match the concept: one container by default, no required external database, local filesystem
storage backend, and LiteDB metadata backend. Configuration should remain environment-variable-friendly for tools such
as Docker Compose and Portainer. The bootstrap admin token must come from environment configuration and must not be
baked into the image.

Document the required mounts and environment variables in `README.md`, including the sensitive nature of the admin token
and the expected persistence locations for blobs and LiteDB data. This plan does not need to add orchestration manifests
beyond what is necessary to explain and verify the one-container deployment.
