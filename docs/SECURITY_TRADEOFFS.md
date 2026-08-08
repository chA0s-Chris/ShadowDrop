# Security Trade-offs

ShadowDrop encrypts content on the sender's machine with AES-256-GCM and
stores only ciphertext on the server. What varies between the modes below is
**how the decryption key travels** and what a leaked artifact is worth to an
attacker. This page explains those trade-offs; the operational mitigations
live in [Deployment Hardening](DEPLOYMENT_HARDENING.md).

## Separate-key mode (default)

A default `upload` produces two artifacts with deliberately split value:

- the **share URL** — public reference to the ciphertext; useless without the
  key, and
- the **share key** — printed as `share-key:` (or written to a file with
  `--secrets-out`).

Deliver them over **different channels** (e.g. URL by e-mail, key by
messenger). An attacker must compromise both channels to read the content;
the server can never decrypt it because the key never reaches the server.
This is the recommended mode whenever the recipient can run the CLI.

`--secrets-out` keeps credentials off the terminal (and out of shell history
and scrollback). `--embed-secrets` does the opposite for queues: it produces a
single self-contained queue file that is as sensitive as the shared files
themselves — treat it like the plaintext. A queue's `credentials` object is
scoped to the one share the queue describes, so leaking the file exposes exactly
that share; the queue carries no credential for anything else.

A generated queue also records the uploader's **directory layout** relative to
the upload root: destinations such as `clients/acme/contract.pdf` reveal folder
names even though the file contents stay encrypted until the recipient decrypts
them. When the layout itself is sensitive, upload with `--flatten` so only leaf
names are recorded, or set recipient-facing names with `--name`/`--display-name`.

## Direct-HTTP mode (`--direct-http`)

Direct-HTTP shares exist for recipients who cannot run the CLI. They send the
decryption key to the server via the `sd-key` query parameter or the
`ShadowDrop-Key` header, and the server decrypts the file before streaming the
response. The emitted `download-url` embeds the key material in `sd-key`, so
**possession of the URL equals possession of the file**:

```text
https://…/d/<share-token>/files/<file-id>?sd-key=<base64-key>
```

Complete URLs are routinely recorded: browser history, HTTP referrer headers,
chat previews, proxy and access logs, and request tracing all retain the key
material — potentially long after the download. See
[Direct-HTTP download URL sensitivity](DEPLOYMENT_HARDENING.md#direct-http-download-url-sensitivity)
for the full guidance, including revoking a share after a suspected exposure.

### `curl-command`: header-based key delivery

For command-line recipients, direct-HTTP uploads also emit a `curl-command`
that sends the key in the `ShadowDrop-Key` **header** and keeps `sd-key` out
of the URL:

```bash
curl -H 'ShadowDrop-Key: 0+9Ol9…' 'https://…/d/<share-token>/files/<file-id>' -o 'report.pdf'
```

Headers do not land in URL-based logs or browser history, so prefer the
`curl-command` over the `download-url` whenever the recipient has a shell.

## Bearer tokens

### Download bearer tokens

`upload --download-token` (separate-key shares only) generates an additional
download bearer token. Downloads then require both the share key **and**
`--bearer-token <token>`, giving you a second, independently deliverable
credential. The download CLI accepts the token only as a command-line
argument.

### Scoped upload credentials

The CLI's routine upload token (`--upload-token`,
`SHADOWDROP_UPLOAD_TOKEN`, or config-file `uploadToken`) should be a scoped
credential created by an administrator. It has one fixed `upload-and-share`
capability and can call only `/api/uploads/*` and `POST /api/shares`; it cannot
revoke arbitrary shares, run cleanup, manage credentials, or call other admin
operations.

Each credential owns its reservations and completed files. One credential
cannot inspect, upload against, or share another credential's records, and it
cannot claim legacy ownerless records. The bootstrap admin token is accepted on
the scoped routes for migration/recovery and can use both ownerless and owned
records, so it remains a root credential and should not be distributed to
routine uploaders.

Credentials may expire and may cap encrypted bytes per file and per share.
The share cap is calculated from immutable encrypted file lengths. This release
does not implement request-count quotas or consumable byte budgets; use upstream
rate/traffic controls when those limits matter.

`shadowdrop token create` displays the plaintext token exactly once. The server
persists only non-reversible secret material and list/inspect never reveal the
token, hash, salt, or lookup digest. Put the token directly into a secret
manager or protected client configuration and keep it out of logs and shell
history. Credential names and management IDs are administrative metadata, not
authentication secrets, but should not be published unnecessarily.

Expiration or revocation blocks new authenticated operations. It does not
delete uploaded data or revoke shares already created with the credential;
revoke those shares separately when required.

### Admin credentials

Credential management, share revocation, and cleanup use the bootstrap admin
token through `--admin-token`, `SHADOWDROP_ADMIN_TOKEN`, or config-file
`adminToken`. Administrative commands deliberately never fall back to the
upload-token setting. Keep this token on the management boundary described in
[Deployment Hardening](DEPLOYMENT_HARDENING.md#admin-endpoint-exposure).

`GET /api/admin/shares` and `shadowdrop share list` are administrative inventory
surfaces. They deliberately expose stable share IDs, lifecycle timestamps and
statuses, normalized latest cleanup outcomes, file counts, and retained
ciphertext byte totals. They never expose filenames, upload-owner or credential
identifiers, share/download token material or hashes, blob keys, cryptographic
metadata, plaintext hashes, persistence records, provider details, or exception
text. Treat the returned IDs and lifecycle history as sensitive operational
metadata even though they are not download capabilities. Share-list audit events
contain only operation, outcome, HTTP status, and elapsed time; query values,
cursors, identifiers, results, and exceptions are excluded.

`GET /api/admin/shares/{shareId}` and `shadowdrop share inspect <share-id>` add an ordered, allow-listed per-file retention view. Both
filename properties remain `null` by default and are disclosed only through the explicit `includeFilenames=true` or
`--include-filenames` opt-in. Filenames can reveal personal, business, or host information and must be treated as sensitive; inspection
audits record only whether disclosure was requested, never the filenames, share ID, query, result, token material, cryptographic data,
storage identifiers, or provider exceptions. Internal share IDs and inspection results never replace the public share token at the
download boundary and cannot be used as download capabilities.

Expired and revoked shares are hidden at the token-lookup boundary immediately, independent of the cleanup schedule. Cleanup claims every
file before deleting anything, then removes uploaded-file and share metadata only after all ciphertext is deleted or confirmed absent.
Failures retain the metadata as `cleanup-failed` so operators can diagnose and retry them; successful cleanup removes filenames, hashes,
KDF salts, owner credential IDs, and other per-file metadata instead of retaining a historical record.

The same run reclaims completed uploads that no share references once they are older than
`ShadowDrop:Cleanup:UnreferencedUploadRetention` (seven days by default). Until then, an upload whose share creation was abandoned keeps
its ciphertext and its per-file metadata at rest, so the retention is a deliberate exposure window: shorten it to narrow that window,
lengthen it to keep recovery material available. Reclamation takes the same durable per-file claim cleanup takes and re-checks share
references behind it, so it can never delete ciphertext a share still points at — including an expired or revoked share awaiting purge —
and never touches an upload reservation. Its failures are logged with the file identifier alone, never the blob key or file name.

Those claims are themselves a metadata store: while a share creation is in flight, `share_operation_claims` holds the proposed share
record — filenames, share and download token hashes, and the owner credential ID — so an interrupted creation can be resolved without
exposing its files to cleanup. The claim is deleted once the operation finishes, but it survives a process failure until a later run
resolves it, so protect and back up that collection exactly like the share and uploaded-file metadata it mirrors.

### Status projection sensitivity

`GET /api/status` is intentionally public and coarse: it exposes only protocol version, liveness/readiness, a stable allow-listed reason,
and capability booleans. `GET /api/status/upload` accepts only a scoped upload credential—not the bootstrap admin token—and adds only that
credential's effective limits and expiry. Exact build/provider data, retained-storage totals, share counts, and cleanup state require the
admin credential at `GET /api/admin/status`.

No status tier returns credentials, token material or hashes, credential identifiers, connection details, database hosts, storage paths or
keys, raw configuration, or internal exception text. The CLI does not let credentials found only in environment or configuration silently
elevate the public status tier. A failed credential-provider lookup yields a public-safe bodyless `503`, and admin status audit records are
restricted to operation, outcome, HTTP status, and elapsed time.

## `--insecure` versus `--cacert`

When the server presents a certificate the CLI does not trust (self-signed,
private CA):

- `--cacert <pem>` (or `SHADOWDROP_CACERT`) adds the given certificate as an
  additional trust anchor. The presented chain is **still validated** — this
  is the safe option and should be your default for lab or internal setups.
- `-k`/`--insecure` (or `SHADOWDROP_INSECURE=1|true|yes`) disables certificate
  validation entirely. A man-in-the-middle can then read upload/admin tokens,
  download bearer tokens, and any direct-HTTP key material in transit. Use it
  only for throwaway local testing, never with real credentials.

Once `SHADOWDROP_INSECURE` is set to a truthy value there is no flag to force
validation back on for a single invocation — unset the variable instead.
