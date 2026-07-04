# Deployment Guide

This guide covers running the ShadowDrop API server as a container. For the
security boundaries an operator must decide on before going live, read
[Deployment Hardening](DEPLOYMENT_HARDENING.md) alongside this page.

## Image and tags

The API server is distributed exclusively as the Docker Hub image
[`chaos/shadowdrop`](https://hub.docker.com/r/chaos/shadowdrop). There is no
other image registry.

Tags follow the usual semantic-versioning convenience scheme:

| Tag      | Meaning                                                        |
| -------- | -------------------------------------------------------------- |
| `1.2.3`  | Exactly this version. Immutable once published.                |
| `1.2`    | The highest patch release of the `1.2` minor line.             |
| `1`      | The highest `1.x.x` release.                                   |
| `latest` | The latest production version. Never points to a pre-release.  |

Pre-releases (e.g. `1.2.3-rc.1`) only ever get their exact-version tag; they
never move `latest` or the floating major/minor tags.

> **Note:** Release publishing is not wired up yet (see the MVP limitations in
> the [README](../README.md)). Until the first release is pushed, build the
> image locally with `bash build.sh BuildDockerImage`, which produces
> `shadowdrop:<version>`.

## Running the container

```bash
docker run -d --name shadowdrop \
  -p 19423:19423 \
  -v shadowdrop-data:/app/data \
  -e SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN="use-a-long-random-secret" \
  chaos/shadowdrop:latest
```

### Port `19423`

The image serves plain HTTP on port `19423` (`ASPNETCORE_HTTP_PORTS=19423` is
baked into the image, and the port is `EXPOSE`d). The container does not
terminate TLS itself — see [TLS and reverse proxies](#tls-and-reverse-proxies).

### `/app/data` persistence

All server state lives under `/app/data`, which the image declares as a
volume:

- `/app/data/metadata/shadowdrop.db` — the LiteDB metadata database (shares,
  file metadata, the hashed admin credential).
- `/app/data/storage/` — the encrypted blobs.

Mount a named volume or host directory there; losing `/app/data` loses all
shares, uploaded ciphertext, and the stored admin credential. The container
runs as a non-root user and keeps the data directory owner-only.

### `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`

On the **first** start (an empty `/app/data`), the server requires the
`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` environment variable and refuses to start
without it. The token is hashed with PBKDF2 and persisted in the metadata
database; the plaintext is never stored.

On subsequent starts the stored credential is used and the environment
variable is ignored — changing it later does **not** rotate the admin token.
The token authenticates all `/api/admin/*` operations, including CLI uploads
(see [Security Trade-offs](SECURITY_TRADEOFFS.md)). Use a long random secret
and treat it like a root credential.

### Download-only deployments

A server that only needs to serve downloads can disable the admin surface
entirely:

```bash
docker run -d --name shadowdrop \
  -p 19423:19423 \
  -v shadowdrop-data:/app/data \
  -e ShadowDrop__ApiExposure__EnableAdminOperations=false \
  chaos/shadowdrop:latest
```

With admin operations disabled, `/api/admin/*` is not mapped and the bootstrap
token is not required. See
[Deployment Hardening](DEPLOYMENT_HARDENING.md#recommended-mitigations) for
when to choose this shape.

## TLS and reverse proxies

Run a reverse proxy (Caddy, nginx, Traefik, an ingress controller, …) in front
of the container and terminate TLS there; forward traffic to the container's
port `19423` over the internal network. Never expose plain HTTP publicly —
share URLs and download credentials travel in requests and responses.

The reverse proxy must also enforce the admin-route restrictions described in
[Deployment Hardening](DEPLOYMENT_HARDENING.md#reverse-proxy-controls):
untrusted clients should not reach `/api/admin/*` at all, and allowed clients
should be rate-limited.

### Public hostname and generated URLs

The server does not know its public hostname. Share URLs and direct-HTTP
download URLs are generated **by the CLI** from the server URL the CLI was
configured with (`--server-url`, `SHADOWDROP_SERVER_URL`, or the config file —
see the [CLI guide](CLI.md#configuration)). Always configure the CLI with the
public, TLS-terminated hostname (e.g. `https://drop.example.com`), not an
internal address — otherwise the URLs you hand to recipients will point at a
host they cannot reach.

## Health check

The server exposes `GET /health` for liveness probes.
