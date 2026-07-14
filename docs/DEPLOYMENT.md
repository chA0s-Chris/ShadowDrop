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

## Docker Compose deployments

ShadowDrop provides two explicit single-host Compose options:

| File                          | Metadata | Encrypted blobs | Persistent volume                        |
|-------------------------------|----------|-----------------|------------------------------------------|
| `docker/compose.local.yaml`   | LiteDB   | Filesystem      | One volume mounted at `/app/data`        |
| `docker/compose.mongodb.yaml` | MongoDB  | GridFS          | One MongoDB volume mounted at `/data/db` |

The MongoDB + GridFS option is a convenient single-host deployment. Its one
MongoDB service is not a replica set and provides no highly available MongoDB
topology. Use an independently managed MongoDB deployment when availability
requirements exceed a single host.

Both files share the Compose project name `shadowdrop` and publish the same
host port, so run only one variant per host. To switch variants, stop the
running one first with
`docker compose --env-file docker/.env -f docker/compose.<variant>.yaml down`;
named volumes are preserved.

Copy the environment contract and fill in the values required by the variant:

```bash
cp docker/.env.example docker/.env
chmod 600 docker/.env
```

For the local variant, set `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`. Then start it:

```bash
docker compose --env-file docker/.env -f docker/compose.local.yaml up -d
```

For MongoDB + GridFS, also set `MONGO_INITDB_ROOT_USERNAME`,
`MONGO_INITDB_ROOT_PASSWORD`, and the complete
`SHADOWDROP_MONGO_CONNECTION_STRING`. The connection string must address the
Compose service name `mongodb` and authenticate against `admin`, for example
`mongodb://<user>:<password>@mongodb:27017/?authSource=admin`. Compose passes
that complete operator-supplied value to ShadowDrop without reconstructing it.

```bash
docker compose --env-file docker/.env -f docker/compose.mongodb.yaml up -d
```

Both files bind `127.0.0.1:19423` by default. This is appropriate for a reverse
proxy on the same host. For intentional LAN access, set
`SHADOWDROP_BIND_ADDRESS=0.0.0.0` in `.env`, apply host-firewall restrictions,
and recreate the service. Do not make that change for direct Internet exposure.

To render and validate the files without printing substituted values, use
non-secret test-only configuration:

```bash
SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN=config-test \
  docker compose -f docker/compose.local.yaml config --quiet

SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN=config-test \
MONGO_INITDB_ROOT_USERNAME=config-test \
MONGO_INITDB_ROOT_PASSWORD=config-test \
SHADOWDROP_MONGO_CONNECTION_STRING='mongodb://config-test:config-test@mongodb:27017/?authSource=admin' \
  docker compose -f docker/compose.mongodb.yaml config --quiet
```

## Running the container without Compose

The existing `docker run` deployment remains supported:

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
The `docker run` example publishes on all host interfaces; use
`-p 127.0.0.1:19423:19423` when only a host-local reverse proxy should connect.

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

## Persistence providers

Metadata and encrypted blobs are selected independently. The defaults remain
LiteDB metadata and filesystem blobs, so existing deployments need no
configuration change.

| Metadata provider | Blob provider | Configuration values |
| --- | --- | --- |
| `LiteDb` | `FileSystem` | `Metadata:LiteDbPath`, `Storage:LocalRoot` |
| `MongoDb` | `FileSystem` | MongoDB settings, `Storage:LocalRoot` |
| `LiteDb` | `MongoGridFs` | `Metadata:LiteDbPath`, MongoDB settings, optional GridFS bucket name |
| `MongoDb` | `MongoGridFs` | MongoDB settings and optional GridFS bucket name |

For example, a fully MongoDB-backed container can be configured as follows:

```bash
docker run -d --name shadowdrop \
  -p 19423:19423 \
  --env-file /secure/path/shadowdrop.env \
  -e ShadowDrop__Metadata__Provider=MongoDb \
  -e ShadowDrop__Storage__Provider=MongoGridFs \
  -e ShadowDrop__Storage__GridFsBucketName=shadowdrop_blobs \
  -e ShadowDrop__Mongo__DatabaseName=shadowdrop \
  chaos/shadowdrop:latest
```

The protected environment file supplies `ShadowDrop__Mongo__ConnectionString`
and `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`. A production orchestrator should inject
the same values through its secret manager. Do not put a credential-bearing
connection string in an image, compose file committed to source control, or
command history. ShadowDrop does not log the MongoDB connection string. It does
log the selected provider names and database name during startup.

MongoDB 5.0 is the initial minimum supported server version. Both standalone
servers and replica sets are supported; the implementation does not depend on
transactions. Sharded clusters have not been validated and are not supported
by this initial release. Re-evaluate the minimum server version whenever
Chaos.Mongo or MongoDB.Driver is upgraded.

When either MongoDB provider is selected, startup verifies connectivity and
creates the required collections/indexes before accepting traffic. Startup
fails if MongoDB is unavailable or initialization fails. A purely local
configuration does not create a MongoDB client and does not require MongoDB
settings.

### Scaling constraints

All four combinations support a single application instance. For multiple
instances:

- MongoDB metadata with GridFS is the standard horizontally scaled setup.
- MongoDB metadata with filesystem blobs is suitable only when `LocalRoot` is
  a shared filesystem mounted consistently on every instance.
- LiteDB metadata combinations remain single-instance configurations unless a
  shared-storage arrangement is separately validated.
- Selecting MongoDB only for GridFS enables distributed cleanup coordination,
  but it does not make LiteDB metadata safe for multiple writers.

MongoDB-backed cleanup uses a leased Chaos.Mongo distributed lock in addition
to the in-process guard. Cleanup remains idempotent if a lease expires or an
instance terminates partway through a run.

### Switching, backup, and restore

Changing a provider selects a different backend; it does **not** migrate data.
Existing LiteDB metadata and filesystem blobs remain where they are until a
separate migration facility is implemented. Plan and validate any provider
change as an explicit data migration.

Back up and restore the active metadata store and blob store as one consistent
set while writes are quiesced or by using a storage-level consistent snapshot.
For `docker/compose.local.yaml`, capture the entire `/app/data` volume as one unit; it
contains the LiteDB metadata, hashed admin credential, and encrypted filesystem
blobs.

For `docker/compose.mongodb.yaml`, use MongoDB-supported backup tooling and include the
ShadowDrop metadata collections (`uploaded_files`, `shares`, and `admin_tokens`),
both GridFS collections (`shadowdrop_blobs.files` and
`shadowdrop_blobs.chunks`), and the Chaos.Mongo distributed-lock collection
from the same consistent backup point. Restore the complete set together before
starting ShadowDrop. A mixed LiteDB/GridFS or MongoDB/filesystem deployment
likewise requires coordinated backups across both systems. Always rehearse a
restore before relying on a backup.

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

The server exposes two unauthenticated health endpoints:

- `GET /health/live` reports that the API process is serving requests.
- `GET /health/ready` reports whether the API can serve its configured workload.
  Local persistence is ready after normal startup. When either MongoDB provider
  is active, readiness performs a short, bounded MongoDB ping and returns HTTP
  503 if MongoDB is unavailable.

Both Compose API services run the shell-free probe included in the image
against `/health/ready`. The MongoDB variant additionally uses an authenticated
MongoDB health check and does not start the API until MongoDB is healthy.

## Compose persistence smoke test

`./build.sh SmokeTestDockerCompose` is an opt-in pre-release check. It builds
the current branch image, exercises both committed Compose files through a
temporary image override, persists representative metadata and encrypted blob
data, recreates the services without deleting volumes, and verifies that the
original admin credential and data remain usable. The target owns uniquely
named Compose resources and removes its override, containers, network, and
volumes even after failure. It is intentionally outside the normal `Test` and
`TestEndToEnd` targets.

On Linux kernel 6.19+ hosts affected by MongoDB `SERVER-121912`, the target's
temporary MongoDB override also sets `GLIBC_TUNABLES=glibc.pthread.rseq=1` so
the test container can run. The committed operator-facing Compose file does not
carry this test-only workaround; MongoDB's production notes still classify
kernel 6.19 as incompatible pending their TCMalloc update.
