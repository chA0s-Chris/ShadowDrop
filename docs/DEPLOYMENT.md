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

### Streaming large uploads through nginx

Every reverse proxy, ingress, load balancer, and CDN in the request path must
permit the complete multipart request and a transfer lasting as long as the
slowest supported connection. The body-size limit needs headroom beyond the
encrypted file itself for multipart boundaries and metadata, and should be
aligned with ShadowDrop's effective Kestrel request-body limit.

The following nginx location is representative; the `5g` body size gives the
default 4 GiB upload limit (`ShadowDrop:Upload:MaxBytes`) headroom for multipart
boundaries and metadata. Adjust the size and timeout values to match your
ShadowDrop configuration and operating policy:

```nginx
location /api/admin/uploads {
    client_max_body_size 5g;
    client_body_timeout 10m;

    proxy_request_buffering off;
    proxy_http_version 1.1;
    proxy_send_timeout 10m;
    proxy_read_timeout 10m;

    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_pass http://shadowdrop:19423;
}
```

`proxy_request_buffering off` streams client request bytes to ShadowDrop
immediately. This is distinct from `proxy_buffering`, which controls response
buffering and does not change upload handling. With request buffering enabled,
nginx may place the entire body in client-body temporary storage before the API
sees the request. Disabling it avoids that temporary-file cost, but nginx can no
longer retry a partially forwarded, non-resumable upload against another
upstream. Explicit HTTP/1.1 upstream proxying also preserves streaming
compatibility with older nginx versions that might otherwise buffer chunked
requests.

`client_body_timeout`, `proxy_send_timeout`, and `proxy_read_timeout` are
inactivity limits between successive I/O operations, not total-transfer
deadlines. Keep them finite to remove stalled connections, while choosing
values suitable for the slowest expected client. Additional proxy layers may
still impose their own body-size, inactivity, or total-duration limits.

For Nginx Proxy Manager, add the server-compatible directives to the Proxy
Host's **Advanced** custom nginx configuration. Align the example body size and
timeouts with the limits configured for that ShadowDrop deployment; depending
on the generated configuration, a dedicated location may need to be expressed
using Nginx Proxy Manager's supported custom-location form.

## Health check

The server exposes `GET /health` for liveness probes.
