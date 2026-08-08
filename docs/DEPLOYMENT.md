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

### Ports `19423` and `19424`

The image serves plain HTTP on port `19423` (`ASPNETCORE_HTTP_PORTS=19423` is
baked into the image, and the port remains `EXPOSE`d). It also advertises port
`19424` for optional [app-managed HTTPS](#app-managed-https). `EXPOSE` is image
metadata: it neither starts an HTTPS listener nor publishes either port. With
no HTTPS configuration, ShadowDrop continues to bind only plain HTTP on
`19423`. The `docker run` example publishes on all host interfaces; use
`-p 127.0.0.1:19423:19423` when only a host-local reverse proxy or health probe
should connect.

### `/app/data` persistence

All server state lives under `/app/data`, which the image declares as a
volume:

- `/app/data/metadata/shadowdrop.db` — the LiteDB metadata database (shares,
  file metadata, hashed upload credentials, and the hashed admin credential).
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
The token authenticates all `/api/admin/*` operations and is also accepted on
the scoped `/api/uploads/*` and `/api/shares` routes for migration and
recovery. Use a long random secret, treat it like a root credential, and
provision narrower upload credentials for routine users and automation:

```bash
export SHADOWDROP_SERVER_URL="https://drop.example.com"
export SHADOWDROP_ADMIN_TOKEN="use-a-long-random-secret"
shadowdrop token create --name "automation" --expires-in 90d \
  --max-file-bytes 1073741824 --max-share-bytes 2147483648
```

The plaintext token is returned only by this creation operation and cannot be
recovered. Store it immediately in the uploader's secret store. Credential
revocation stops new operations but deliberately leaves existing shares and
uploaded data available. See [Security Trade-offs](SECURITY_TRADEOFFS.md).

### API exposure settings

`ShadowDrop:ApiExposure:EnableAdminOperations` controls `/api/admin/*`.
`ShadowDrop:ApiExposure:EnableUploads` is nullable and controls the scoped
`/api/uploads/*` and `/api/shares` routes. When `EnableUploads` is omitted or
`null`, it inherits `EnableAdminOperations`, preserving existing deployment
behavior. Set it explicitly when the two surfaces need different exposure:

```text
ShadowDrop__ApiExposure__EnableAdminOperations=false
ShadowDrop__ApiExposure__EnableUploads=true
```

That shape lets existing scoped credentials upload without exposing admin
operations, but cannot provision or revoke credentials until administration
is enabled on a trusted boundary. Conversely, setting `EnableUploads=false`
keeps routine uploads disabled even when admin operations remain enabled.

### Operational status and monitoring

Use `/health/live` and `/health/ready` for minimal orchestrator probes. Use public `GET /api/status` or `shadowdrop server status` for a
scriptable protocol-versioned preflight; the route remains available even when every download, upload, and admin capability is disabled.
Scoped `GET /api/status/upload` is mapped only with uploads enabled, and administrative `GET /api/admin/status` only with admin operations
enabled. Status dependency collection has a five-second server budget; configure the CLI or reverse proxy with a deadline longer than that.

Administrative storage totals come from persisted retained-blob accounting, never filesystem, GridFS, or S3 inventory scans. After an
upgrade, legacy records with unknown retention state keep totals unavailable with `storage-accounting-incomplete` rather than presenting
an unsafe estimate. Successful cleanup deletes uploaded-file metadata after all blobs are deleted or confirmed absent. Cleanup-run status
is process-local and resets to `not-run` on restart; it is operational context, not durable history or a metrics system.

For bounded administrative inventory, use authenticated `GET /api/admin/shares` or `shadowdrop share list`. Both LiteDB/filesystem and
MongoDB/GridFS installations apply provider-side lifecycle filtering, exact counts, newest-first cursor paging, and one batched file-
metadata projection per page; blob-provider inventory is never scanned. The returned ciphertext total counts only persisted `retained`
blobs. Successfully cleaned shares disappear from the inventory because both their uploaded-file metadata and share record are deleted.
The exact `totalMatching` can change between page requests as shares expire, are revoked, or are deleted. Share-list audits contain only operation, outcome, HTTP status, and elapsed
time. Keep this endpoint on the same protected management boundary as every other `/api/admin/*` route.

Operational audit records for `/api/admin/status`, `/api/admin/shares`, and `/api/admin/shares/{shareId}` are all written by one shared
filter, so they use the log source context `ShadowDrop.Api.Status.OperationalAuditEndpointFilter`. Log pipelines that selected status
audits by the previous `ShadowDrop.Api.Status.AdminStatusAuditEndpointFilter` context need updating; the `Operation` property
(`admin-status-view`, `admin-share-list`, or `admin-share-inspect`) is the stable way to tell them apart.

For one-share diagnosis, use authenticated `GET /api/admin/shares/{shareId}` or `shadowdrop share inspect <share-id>`. Inspection loads the
share once and performs one bounded batch uploaded-file projection, including on LiteDB/filesystem and MongoDB/GridFS deployments; it does
not enumerate a blob provider. Missing uploaded-file metadata is represented by a zero-byte `missing` entry so a partially completed
cleanup remains diagnosable. Filenames are sensitive and remain `null` unless the caller explicitly selects `includeFilenames=true` or
`--include-filenames`. The `admin-share-inspect` audit record adds only a `FilenamesIncluded` Boolean to the shared operation, outcome,
HTTP-status, and elapsed-time fields. Keep inspection on the protected management boundary and restrict access to its output accordingly.

LiteDB assembles each share-list page by walking equal-creation-time groups, because it orders by a single field. A page therefore costs one
indexed ordering query plus one lookup per distinct creation timestamp it spans. With a lifecycle filter that pushes the query planner off
the creation-time index this stays proportional to collection size; prefer MongoDB for installations where operators page through large
share inventories regularly.

### Unreferenced upload reclamation

Every cleanup run finishes by reclaiming completed uploads that no share
references. Without it, an upload whose share creation was abandoned keeps its
ciphertext and per-file metadata forever, because the share phase only walks
shares.

```bash
ShadowDrop__Cleanup__CronExpression=0 */2 * * *
ShadowDrop__Cleanup__UnreferencedUploadRetention=7.00:00:00
```

`UnreferencedUploadRetention` is a `d.hh:mm:ss` duration and must be positive;
it defaults to seven days. An upload becomes eligible only once its completion
timestamp is at or before `now - retention`. **There is no separate on/off
switch:** to effectively disable reclamation, configure a retention long enough
that nothing ever reaches it (for example `36500.00:00:00`).

The sweep never touches an upload reservation, claimed or unclaimed, an upload
inside the grace period, or a file referenced by any share — including an
expired or revoked share still awaiting purge. A completed upload written before
this feature existed carries no completion timestamp; the first run stamps it
with the current time, so it waits a full grace period from that first sighting
rather than being reclaimed immediately after an upgrade.

Each run inspects at most 200 upload candidates, never-inspected and then
least-recently-inspected first, so a record that keeps failing rotates to the
back of the queue instead of starving fresh ones; a backlog drains across
successive runs. A separate budget of 50 claims per run recovers claims orphaned
by a crash. Reclamation deletes or confirms absent the ciphertext, records the
retained-blob accounting transition, and only then deletes the metadata row, so
a failure at any step retains both the claim and the row for an idempotent
retry. Failures are counted in the cleanup result's `sweepFailures`, included in
the run's `failures` total, and logged with the affected file identifier only.

A share creation that races a cleanup run over one of these eligible files can be
rejected with `Share creation was superseded before it could commit. Retry the
request.` — the sweep resolves a conflicting creation claim before it reclaims,
and a claim being resolved cannot be distinguished from an abandoned one. No
share is ever created over reclaimed ciphertext, so the error is safe to retry:
the retry either succeeds or reports the file as gone.

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
token is not required. Because `EnableUploads` is omitted, it inherits `false`
and the scoped upload/share routes are also not mapped. See
[Deployment Hardening](DEPLOYMENT_HARDENING.md#recommended-mitigations) for
when to choose this shape.

In this mode `GET /api/admin/shares` returns the framework `404`; the CLI reports
the disabled administrative capability as a generic exit-code-`1` failure with
no stdout page.

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
| `LiteDb` | `S3` | `Metadata:LiteDbPath`, S3 bucket name and signing region |
| `MongoDb` | `S3` | MongoDB settings, S3 bucket name and signing region |

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

### AWS S3 and compatible object storage

Select `S3` independently of the metadata provider. A minimal AWS deployment
sets the provider, bucket, and region; omit ShadowDrop's static credential
settings to use the standard AWS SDK credential chain:

```text
ShadowDrop__Storage__Provider=S3
ShadowDrop__Storage__S3__BucketName=shadowdrop-production
ShadowDrop__Storage__S3__Region=eu-central-1
```

The standard chain supports workload/container and instance credentials,
environment credentials (including session tokens), and local AWS profiles.
This is preferred to long-lived keys. When static credentials are unavoidable,
inject both `ShadowDrop__Storage__S3__AccessKeyId` and
`ShadowDrop__Storage__S3__SecretAccessKey` through an orchestrator secret or a
mode-`0600` environment file. Never put them in an image, committed Compose
file, command line, or shell history. ShadowDrop logs only whether static
configuration or the AWS credential chain is in use; it never logs access
keys, secret keys, or session tokens.

For RustFS and similar S3-compatible services, also set an absolute service
endpoint and usually enable path-style addressing. `us-east-1` is the
conventional signing-region placeholder when the service does not otherwise
use AWS regions:

```text
ShadowDrop__Storage__Provider=S3
ShadowDrop__Storage__S3__BucketName=shadowdrop
ShadowDrop__Storage__S3__Region=us-east-1
ShadowDrop__Storage__S3__ServiceEndpoint=https://rustfs.example.internal:9000
ShadowDrop__Storage__S3__UsePathStyle=true
ShadowDrop__Storage__S3__KeyPrefix=production/shadowdrop
```

The region remains required with a custom endpoint because it supplies the
SigV4 signing region. AWS endpoints use HTTPS automatically. Give compatible
services a certificate trusted by the ShadowDrop container and use HTTPS in
production; reserve plain HTTP endpoints for isolated development networks.
`UsePathStyle` is normally `false` for AWS and `true` for a single RustFS
endpoint. ShadowDrop does not create or alter the bucket at startup, so create
it with deployment or test administration tooling before starting the API.

The optional `KeyPrefix` is normalized only by trimming surrounding whitespace
and leading/trailing slashes; internal characters and repeated separators are
preserved. It is applied to S3 object requests but is not stored in metadata.
Changing `Storage:Provider` or `Storage:S3:KeyPrefix` redirects future lookups
and writes and immediately strands existing blobs under the previous backend
or prefix. ShadowDrop performs no automatic copy, reconciliation, or migration.

The application identity needs the following baseline AWS policy. It matches the
minimal AWS example above, which configures no `KeyPrefix` and therefore writes
objects at the bucket root. Replace the bucket name with your own.
`s3:ListBucket` is deliberately required because S3 otherwise returns `403`
rather than a reliable `404` for a missing object, and the same read-only grant
supports readiness checks.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "InspectShadowDropBucket",
      "Effect": "Allow",
      "Action": "s3:ListBucket",
      "Resource": "arn:aws:s3:::shadowdrop-production"
    },
    {
      "Sid": "ManageShadowDropObjects",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject",
        "s3:AbortMultipartUpload"
      ],
      "Resource": "arn:aws:s3:::shadowdrop-production/*"
    }
  ]
}
```

When a `KeyPrefix` is configured, narrow the object resource to that prefix. A
deployment using `ShadowDrop__Storage__S3__KeyPrefix=production/shadowdrop`
scopes `ManageShadowDropObjects` to
`arn:aws:s3:::shadowdrop-production/production/shadowdrop/*`. The
`s3:ListBucket` resource stays the bare bucket ARN either way, because the
readiness check lists the bucket rather than the prefix.

Uploads use one reusable 8 MiB payload buffer per active upload. Total payload
buffer memory is therefore approximately `8 MiB × concurrent uploads`, plus
AWS SDK overhead and part-number/ETag metadata bounded by S3's 10,000-part
limit. ShadowDrop rejects an S3 deployment whose `Upload:MaxBytes` exceeds
80,000 MiB (about 78.1 GiB), the capacity of 10,000 fixed-size parts. Objects
smaller than one part use a single `PutObject`; larger objects use multipart
upload. Failures and cancellations trigger a best-effort abort, but a process
or host failure can interrupt cleanup. Configure an S3 lifecycle rule to abort
incomplete multipart uploads after an operationally appropriate interval so
abandoned parts cannot accumulate indefinitely.

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

All six combinations support a single application instance. For multiple
instances:

- MongoDB metadata with GridFS is the standard horizontally scaled setup.
- MongoDB metadata with S3 is also suitable for horizontally scaled instances;
  every instance must use the same bucket, prefix, and credentials policy.
- MongoDB metadata with filesystem blobs is suitable only when `LocalRoot` is
  a shared filesystem mounted consistently on every instance.
- LiteDB metadata combinations remain single-instance configurations unless a
  shared-storage arrangement is separately validated.
- Selecting MongoDB only for GridFS enables distributed cleanup coordination,
  but it does not make LiteDB metadata safe for multiple writers.

MongoDB-backed cleanup uses a leased Chaos.Mongo distributed lock in addition
to the in-process guard and extends that lease throughout a running cleanup.
Durable per-file operation claims in the metadata store, rather than the run
lease, prevent share creation from racing blob or metadata deletion. Cleanup
remains idempotent if lease ownership is lost or an instance terminates partway
through a run. Unreferenced-upload reclamation runs under the same lease and
takes the same kind of durable per-file claim, so it is safe on the same terms:
losing the lease stops it from starting further files rather than leaving a
half-deleted one behind.

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
ShadowDrop metadata collections (`uploaded_files`, `shares`,
`share_operation_claims`, `admin_tokens`, and `upload_credentials`),
both GridFS collections (`shadowdrop_blobs.files` and
`shadowdrop_blobs.chunks`), and the Chaos.Mongo distributed-lock collection
from the same consistent backup point. Restore the complete set together before
starting ShadowDrop. A mixed LiteDB/GridFS, MongoDB/filesystem, or metadata/S3
deployment likewise requires coordinated backups across both systems. For S3,
preserve the bucket's objects and versions together with the matching metadata
snapshot; bucket versioning or replication does not make an independently timed
metadata backup consistent. Restore metadata and objects to the same provider
and `KeyPrefix` before starting ShadowDrop. Always rehearse a restore before
relying on a backup.

## TLS and reverse proxies

A reverse proxy (Caddy, nginx, Traefik, an ingress controller, …) remains the
recommended Internet-facing topology because it can automate certificate
issuance and renewal, isolate routes, and enforce request limits. Terminate TLS
there and forward traffic to the container's port `19423` over the internal
network. Never expose plain HTTP publicly — share URLs and download credentials
travel in requests and responses.

The reverse proxy must enforce the route restrictions described in
[Deployment Hardening](DEPLOYMENT_HARDENING.md#reverse-proxy-controls).
Untrusted uploaders may be allowed to reach `/api/uploads/*` and `/api/shares`
without being allowed to reach `/api/admin/*`; administrative clients should
remain on a trusted, rate-limited boundary.

### App-managed HTTPS

Deployments without a reverse proxy can ask Kestrel to terminate TLS with its
standard ASP.NET Core configuration. ShadowDrop does not load certificates in
application code, issue certificates, renew them, redirect HTTP to HTTPS, or
enable HSTS automatically. Kestrel fails the complete application startup when
an HTTPS listener is requested without a readable certificate and matching
private key, or when its password is wrong; it does not silently fall back to
the HTTP listener.

The image's optional HTTPS port is `19424`. The following override augments
either bundled Compose file while preserving its loopback-only port `19423` and
unchanged HTTP healthcheck:

```yaml
# compose.https.yaml
services:
  shadowdrop:
    environment:
      ASPNETCORE_HTTP_PORTS: "19423"
      ASPNETCORE_HTTPS_PORTS: "19424"
      ASPNETCORE_Kestrel__Certificates__Default__Path: /run/secrets/shadowdrop-tls/server.pfx
      ASPNETCORE_Kestrel__Certificates__Default__Password: ${SHADOWDROP_TLS_CERTIFICATE_PASSWORD:?Set SHADOWDROP_TLS_CERTIFICATE_PASSWORD}
    ports:
      - "19424:19424"
    volumes:
      - /srv/shadowdrop/certificates:/run/secrets/shadowdrop-tls:ro
```

Keep the password in a secret manager or protected environment source and
export `SHADOWDROP_TLS_CERTIFICATE_PASSWORD` only for the Compose invocation;
never put its literal value in the override, an image layer, source control, or
shell history. Environment values remain visible to principals that can inspect
the container, so access to the Docker host is a secret boundary. Start the
deployment with, for example:

```bash
docker compose -f docker/compose.local.yaml -f compose.https.yaml up -d
```

The certificate directory is mounted read-only and is not part of the image.
The example uses an absolute host path on purpose: Compose resolves a relative
bind path against the project directory — the directory of the *first* `-f`
file, `docker/` in the command above — not against the location of the override
file itself. The directory, certificate, and private-key permissions must allow
the image's non-root user to read them without granting unnecessary host users
access. The base Compose files publish `19423` on `127.0.0.1`; do not override
that binding to a public address. Only `19424` should be the public application
port in this topology.

For a PEM certificate and separate private key, replace the PFX variables with:

```text
ASPNETCORE_Kestrel__Certificates__Default__Path=/run/secrets/shadowdrop-tls/fullchain.pem
ASPNETCORE_Kestrel__Certificates__Default__KeyPath=/run/secrets/shadowdrop-tls/privkey.pem
ASPNETCORE_Kestrel__Certificates__Default__Password=<encrypted-private-key-password>
```

Omit `Password` only when the PEM key is intentionally unencrypted and its file
permissions provide the required protection. A JSON configuration file can use
the equivalent unprefixed settings (shown here for PFX):

```json
{
  "HTTPS_PORTS": "19424",
  "Kestrel": {
    "Certificates": {
      "Default": {
        "Path": "/run/secrets/shadowdrop-tls/server.pfx",
        "Password": "<inject-at-deployment-time>"
      }
    }
  }
}
```

Do not commit a configuration file containing the real password. Environment
variables use the `ASPNETCORE_` prefix and double underscores; configuration
files use `HTTPS_PORTS` and the `Kestrel:Certificates:Default` hierarchy.

The bundled healthcheck deliberately continues to call
`http://127.0.0.1:19423/health/ready`. `ShadowDrop.HealthProbe` uses the system's
default certificate trust and has no custom-CA option, so it cannot validate a
private or self-signed server certificate. Keep the loopback HTTP listener
bound for that probe even though only HTTPS is published publicly.

Operators own certificate creation, hostname/SAN correctness, trust
distribution, expiry monitoring, renewal, and safe replacement. Replace the
mounted files atomically and restart the container to make the new certificate
effective. Clients must trust the issuing public or private CA and connect with
a hostname present in the certificate. The ShadowDrop CLI can add a private CA
or self-signed certificate with `--cacert <pem>` or `SHADOWDROP_CACERT`; avoid
`--insecure`.

Direct Kestrel HTTPS provides transport encryption, but not a reverse proxy's
route isolation, throttling, or certificate automation. Configure the
`ShadowDrop__ApiExposure__EnableAdminOperations`,
`ShadowDrop__ApiExposure__EnableUploads`, and
`ShadowDrop__ApiExposure__EnablePublicDownloads` toggles so only required API
surfaces are mapped. The health routes are always mapped, so keeping port
`19423` on loopback is the only application-free boundary for the bundled
probe. See [Deployment Hardening](DEPLOYMENT_HARDENING.md#direct-kestrel-https)
before exposing `19424`.

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
location /api/uploads {
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

### Pre-v1 scoped-route migration

The scoped credential release removes the previous upload and share-creation
operations under `/api/admin/uploads` and `POST /api/admin/shares` without
compatibility aliases. This is an intentional pre-v1 breaking change. Update
direct API clients to `/api/uploads/*` and `POST /api/shares`; existing
bootstrap admin tokens remain valid there. CLI upload configuration keeps the
existing `--upload-token`, `SHADOWDROP_UPLOAD_TOKEN`, and `uploadToken` names,
while administrative CLI commands now require `--admin-token`,
`SHADOWDROP_ADMIN_TOKEN`, or `adminToken` with no upload-token fallback.

## Health check

The server exposes two unauthenticated health endpoints:

- `GET /health/live` reports that the API process is serving requests.
- `GET /health/ready` reports whether the API can serve its configured workload.
  Local persistence is ready after normal startup. When either MongoDB provider
  is active, readiness performs a short, bounded MongoDB ping. When S3 is the
  blob provider, readiness also performs a bounded, read-only bucket listing
  with a zero-key limit. Checks are composed, so a MongoDB/S3 deployment returns
  HTTP 503 if either dependency is unreachable, misconfigured, or inaccessible.

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
