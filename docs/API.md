# ShadowDrop HTTP API

This page is an orientation guide to the complete HTTP surface of `ShadowDrop.Api`: every
endpoint with its route, method, purpose, audience, and required authentication. It is
deliberately **not** a schema reference — request and response shapes live in the endpoint
classes under `src/ShadowDrop.Api` and change more often than routes do. For the client-side
view of these flows, see the [CLI guide](CLI.md).

A drift-guard test (`ApiDocumentationTests` in `ShadowDrop.Api.Tests`) asserts that every
route pattern registered by the API appears in this document, so new endpoints cannot be
added without updating this page. HTTP methods are documented here but verified by review.

## Audiences and credentials

ShadowDrop distinguishes three callers, each with its own credential:

- **Downloader** — an anonymous share recipient. There is no account: authorization is the
  unguessable share token embedded in the download URL. If the share was created with a
  download bearer token, the recipient must additionally send it as `Authorization: Bearer`.
- **Uploader** — holds an *upload credential*, a scoped token in the reserved `sdu1.`
  namespace, sent as `Authorization: Bearer`. Upload credentials are created and revoked by
  the admin (see [Admin](#admin)).
- **Admin** — holds the management key (the bootstrap admin token, configured via the
  `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` environment variable), sent as `Authorization: Bearer`.
  The management key is also accepted on all uploader routes.

Health probes and the coarse public status projection are unauthenticated and aimed at operators and orchestration platforms.

### What a scoped upload credential can reach

Upload credentials are **owner-bound**: every file reservation, upload, and share created
with a credential is tagged with that credential's ID, and the credential can only read,
share, and act on resources it owns. Metadata lookups for files owned by another credential
return `404` as if the file did not exist. A credential can optionally carry an expiry and
per-file / per-share size ceilings, which tighten (never widen) the server-wide limits
reported by the capabilities endpoint.

The management key acts as *bootstrap admin* on uploader routes: it is not owner-bound and
can reach all resources, including ownerless ones created before scoped credentials existed.

### Exposure toggles

Route groups are only registered when the corresponding `ApiExposure` option is enabled —
on a deployment with a group disabled, its routes return `404`:

| Option                  | Routes                            | Default                         |
|-------------------------|-----------------------------------|---------------------------------|
| `EnablePublicDownloads` | `/d/...`                          | enabled                         |
| `EnableUploads`         | `/api/uploads/...`, `/api/shares`, `/api/status/upload` | follows `EnableAdminOperations` |
| `EnableAdminOperations` | `/api/admin/...`                                     | enabled                         |

The `/health` routes and public `/api/status` route are always registered.

## Health

**Audience:** ops / anyone · **Auth:** none

| Method | Route           | Purpose                                                                           |
|--------|-----------------|-----------------------------------------------------------------------------------|
| `GET`  | `/health/live`  | Liveness probe — `200` whenever the process is serving requests.                  |
| `GET`  | `/health/ready` | Minimal readiness probe — verifies every configured metadata and blob-storage dependency; `503` when any is unavailable. |

## Operational status

Status protocol version `1` gives scripts a bounded preflight without exposing arbitrary health-check details. Every response includes
`protocolVersion`, `live`, `ready`, a stable `reason`, and capability flags. Ready responses use `200`; dependency degradation uses `503`
with the same selected projection. Reasons are limited to `none`, `dependency-timeout`, `dependency-unavailable`, and
`capability-disabled` where applicable. Independent probes and administrative statistics share a five-second server collection budget.

| Method | Route                | Audience / authentication | Projection |
|--------|----------------------|---------------------------|------------|
| `GET`  | `/api/status`        | anyone; no credential     | Coarse liveness, readiness, reason, and capability availability. |
| `GET`  | `/api/status/upload` | scoped uploader only; bootstrap admin is rejected | Public fields plus effective file/share byte limits and nullable credential expiry. |
| `GET`  | `/api/admin/status`  | admin only                | Build/uptime, allow-listed components and providers, retained storage, share counts, cleanup state, resumable-session availability, and warnings. |

Invalid, expired, or revoked upload credentials return `401`. If the credential metadata provider is unavailable, upload-status
authentication returns a bodyless `503` rather than disclosing limits or returning an internal exception. Disabled upload/admin status
routes are not registered and return `404`.

Administrative storage totals count blobs whose persisted retention state is `retained`; successful or already-missing cleanup changes it
to `deleted`. Records written before retention accounting are `unknown`, so both totals are `null` and
`configurationWarnings` contains `storage-accounting-incomplete` until exact accounting is possible. Share lifecycle counts and cleanup
counts are independent predicates and may overlap. Cleanup history is process-local: restart resets it to `not-run`.

Status responses never contain credentials, hashes, credential IDs, connection strings, hosts, paths, object keys, raw configuration, or
exception text. Exact build and provider names are admin-only. Successful and failed admin status attempts emit an `admin-status-view`
audit event containing only operation, outcome, HTTP status, and elapsed time.

## Downloads

**Audience:** downloader (anonymous, token-based) · **Auth:** share token in the URL path;
`Authorization: Bearer` with the share's download bearer token when the share has one

| Method | Route                            | Purpose                                                                                                                                      |
|--------|----------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------|
| `GET`  | `/d/{token}`                     | Resolve a share to its manifest — the shared files with their metadata. `401` for unknown or expired shares.                                 |
| `GET`  | `/d/{token}/files/{fileId:guid}` | Download a file's content. Supports a direct-HTTP mode (server-assisted, browser-friendly) and a streamed CLI mode, both with range support. |

In direct-HTTP mode, clients supply decryption key material either in the `ShadowDrop-Key`
header (CLI and scripts) or the `sd-key` query parameter (browsers). In streamed CLI mode no
key material is sent — the ciphertext is streamed as-is and decrypted client-side. See
[security trade-offs](SECURITY_TRADEOFFS.md) for what each mode reveals to the server.

## Uploads

**Audience:** uploader · **Auth:** `Authorization: Bearer` with an upload credential or the
management key (enforced by the endpoint filter in `UploadOrAdminBearerTokenEndpointFilterExtensions`)

| Method | Route                        | Purpose                                                                                                            |
|--------|------------------------------|--------------------------------------------------------------------------------------------------------------------|
| `GET`  | `/api/uploads/capabilities`  | Report the effective upload limits for the caller — server-wide limits tightened by the credential's own ceilings. |
| `POST` | `/api/uploads/reservations`  | Reserve a file ID for a subsequent upload, owned by the calling credential.                                        |
| `POST` | `/api/uploads`               | Upload an encrypted file payload (multipart) under a reserved file ID.                                             |
| `GET`  | `/api/uploads/{fileId:guid}` | Fetch metadata of an uploaded file. `404` unless the caller owns the file (or is the admin).                       |

## Shares

**Audience:** uploader · **Auth:** same as [Uploads](#uploads)

| Method | Route         | Purpose                                                                                                                     |
|--------|---------------|-----------------------------------------------------------------------------------------------------------------------------|
| `POST` | `/api/shares` | Create a share referencing previously uploaded files owned by the caller; returns the share ID and download token material. |

## Admin

**Audience:** admin · **Auth:** `Authorization: Bearer` with the management key (enforced by
the endpoint filter in `AdminBearerTokenEndpointFilterExtensions`)

| Method | Route                                                      | Purpose                                                                                                    |
|--------|------------------------------------------------------------|------------------------------------------------------------------------------------------------------------|
| `GET`  | `/api/admin/management/ping`                               | Connectivity and credential check for management tooling.                                                  |
| `POST` | `/api/admin/shares/cleanup`                                | Trigger a cleanup run for expired shares; reports the outcome, skipping when a run is already in progress. |
| `POST` | `/api/admin/shares/{shareId:guid}/revoke`                  | Revoke a share so its download token stops resolving. `404` for unknown shares.                            |
| `POST` | `/api/admin/upload-credentials`                            | Create a scoped upload credential; the credential token is returned exactly once in the response.          |
| `GET`  | `/api/admin/upload-credentials`                            | List upload credentials, newest first, with cursor-based paging (`cursor`, `limit`).                       |
| `GET`  | `/api/admin/upload-credentials/{credentialId:guid}`        | Inspect a single upload credential's metadata (never the token).                                           |
| `POST` | `/api/admin/upload-credentials/{credentialId:guid}/revoke` | Revoke an upload credential so its token stops authenticating.                                             |
