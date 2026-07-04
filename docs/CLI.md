# CLI Guide

The ShadowDrop CLI (`shadowdrop`) encrypts files locally, uploads the
ciphertext, creates shares, and downloads and decrypts shared content. Run
`shadowdrop --help` or `shadowdrop <command> --help` for the full option
reference; this guide covers installation, configuration, and the common
workflows.

## Installation

Download the binary for your platform from the
[GitHub releases](https://github.com/chA0s-Chris/ShadowDrop/releases). Each
release ships single-file native binaries named
`shadowdrop-cli-<version>-<platform>` for `linux-x64`, `linux-arm64`,
`osx-x64`, `osx-arm64`, `win-x64`, and `win-arm64` (Windows binaries end in
`.exe`), plus a `CHECKSUMS.sha256` file.

Verify the checksum, then install the binary as `shadowdrop`:

```bash
VERSION=1.0.0
curl -LO "https://github.com/chA0s-Chris/ShadowDrop/releases/download/v${VERSION}/shadowdrop-cli-${VERSION}-linux-x64"
curl -LO "https://github.com/chA0s-Chris/ShadowDrop/releases/download/v${VERSION}/CHECKSUMS.sha256"
sha256sum -c --ignore-missing CHECKSUMS.sha256
install -m 755 "shadowdrop-cli-${VERSION}-linux-x64" ~/.local/bin/shadowdrop
```

> **Note:** Release publishing is not wired up yet (see the MVP limitations in
> the [README](../README.md)). Until the first release lands, build the CLI
> locally with `bash build.sh PublishCli`; the binaries appear under
> `artifacts/publish/cli/`.

## Commands

| Command                        | Purpose                                                        |
| ------------------------------ | -------------------------------------------------------------- |
| `upload [files]`               | Encrypt, upload, and create a share in one step (files are optional only with `--interactive`). |
| `upload raw <files>`           | Encrypt and upload only; prints file IDs and the share key.    |
| `share create <file-ids>`      | Create a share from previously uploaded file IDs.              |
| `share revoke <share-id>`      | Revoke a share by internal share ID.                           |
| `share cleanup`                | Delete server blobs for expired and revoked shares.            |
| `queue create [share-token]`   | Write a download queue file for an existing share.             |
| `download [share-token]`       | Download and decrypt a shared file (or `--queue <file>`).      |

## Configuration

The server URL and upload token are resolved from three sources, highest
precedence first:

1. Command-line flags: `--server-url`, `--upload-token`
2. Environment variables: `SHADOWDROP_SERVER_URL`, `SHADOWDROP_UPLOAD_TOKEN`
3. Config file: `~/.config/shadowdrop/config.json`

```json
{
  "serverUrl": "https://drop.example.com",
  "uploadToken": "use-a-long-random-secret"
}
```

The **upload token is the server's admin bearer token** (the
`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` from the
[deployment guide](DEPLOYMENT.md#shadowdrop_bootstrap_admin_token)). The CLI
sends it as `Authorization: Bearer …` to `/api/admin/uploads`; there is no
separate upload-token provisioning in the MVP. The consequence: whoever can
upload can administer the server, and the CLI must be able to reach the admin
exposure boundary described in
[Deployment Hardening](DEPLOYMENT_HARDENING.md). Prefer the environment
variable or config file over `--upload-token` — command lines can be visible
to process inspection tools.

TLS trust is configured separately and is deliberately **not** read from the
config file:

- `--cacert <pem>` (or `SHADOWDROP_CACERT`) — trust an additional PEM-encoded
  anchor (self-signed server certificate or private CA). The chain is still
  validated; this is the safe option for self-signed reverse proxies.
- `-k`/`--insecure` (or `SHADOWDROP_INSECURE=1|true|yes`) — disable
  certificate validation entirely. Unsafe; see
  [Security Trade-offs](SECURITY_TRADEOFFS.md#--insecure-versus---cacert).

### Generated URLs use the CLI's server URL

`share-url` and direct-HTTP `download-url` values are built from the server
URL the CLI resolved for the invocation — the server does not inject its own
hostname. Behind a reverse proxy, configure the CLI with the public
TLS-terminated hostname (`https://drop.example.com`), not an internal address,
or the generated URLs will be unreachable for recipients.

The examples below assume:

```bash
export SHADOWDROP_SERVER_URL="https://drop.example.com"
export SHADOWDROP_UPLOAD_TOKEN="use-a-long-random-secret"
```

## Uploading and sharing

### Default upload (separate-key share)

```bash
shadowdrop upload report.pdf
# share-url:https://drop.example.com/d/qHxI_3N1cTzPNkt1WSi2rieBiSi858y-OA1Sc_OQlz4
# share-key:5f4a5a7048d41e66dd2833126184beefa46ecf4e9c3c49091a1aafb2e7acfa78
```

Send the recipient the `share-url`; deliver the `share-key` over a different
channel. Shares expire after 7 days by default; use `--expires-in` (e.g.
`--expires-in 12h`, `30m`, `14d`) to change that. Add `--download-token` to
additionally require a download bearer token, `--name`/`--display-name` to set
recipient-facing file names, and `--json` for machine-readable output.

### Direct-HTTP upload (no CLI needed to download)

```bash
shadowdrop upload report.pdf --direct-http
# share-url:https://drop.example.com/d/ZDYBtjkGot4OX-9sy9YaFpHsmndeMG5Ael-a9F5sygQ
# download-url:https://drop.example.com/d/ZDYBtjkGot4OX-9sy9YaFpHsmndeMG5Ael-a9F5sygQ/files/d16c25c8-…?sd-key=0%2B9Ol9…
# curl-command:curl -H 'ShadowDrop-Key: 0+9Ol9…' 'https://drop.example.com/d/…/files/d16c25c8-…' -o 'report.pdf'
```

The `download-url` works in a browser but carries the decryption key in the
`sd-key` query parameter, and the `curl-command` sends the same key in the
`ShadowDrop-Key` header. In both cases the server receives the key and decrypts
the file before streaming it, so the URL or command is as sensitive as the file
itself. Prefer the `curl-command` because it keeps the key out of URL-based
logs and browser history. Read [Security Trade-offs](SECURITY_TRADEOFFS.md)
before choosing this mode.

### Writing credentials to a file: `--secrets-out`

```bash
shadowdrop upload report.pdf --secrets-out secrets.json
# share-url:https://drop.example.com/d/8UeBnPkose2jZGWmFRbQuyET_Qj9Yvypszk5fHkBhp0
# secrets-file:/home/alice/secrets.json
```

`secrets.json` holds the `shareKey` (and `downloadBearerToken`, if one was
requested) so credentials never hit the terminal. Use `--force` to overwrite
an existing file.

### Two-step flow: `upload raw` + `share create`

```bash
shadowdrop upload raw a.bin b.bin
# file-id:…  (one per file)
# share-key:…
shadowdrop share create <file-id-1> <file-id-2> --expires-in 3d
```

## Downloading

### Separate-key download

The recipient passes the share URL (or bare share token plus `--server-url`)
and the key. The decrypted content is written to **stdout**:

```bash
shadowdrop download "https://drop.example.com/d/qHxI_…" \
  --share-key 5f4a5a7048d41e66dd2833126184beefa46ecf4e9c3c49091a1aafb2e7acfa78 \
  > report.pdf
```

`--share-key-file <path>` reads the key from a file instead. If the share
holds multiple files, pick one with `--file <file-id>` — or use a queue to
download all of them to disk. If the share was created with
`--download-token`, the recipient must also pass `--bearer-token <token>`
(this value is only ever taken from the command line).

### Download queues (multi-file, resumable)

A queue is a JSON file describing every file in a share and where to put it.
Create one at upload time with `--queue-out`, or later:

```bash
shadowdrop queue create "https://drop.example.com/d/8UeBnPkose…" --out queue.json
```

By default the queue is **secret-free** — the recipient supplies the key:

```bash
shadowdrop download --queue queue.json \
  --share-key 7510130e70eedda83f0e98ac1350380e322a69654fb1651febce755a6af37bdc \
  --output-root incoming
```

Files land under `--output-root` (default: the current directory) at each
entry's `outputPath`. Interrupted downloads resume where they left off.

With `--embed-secrets` (on `upload`, or on `queue create`) the credentials are
embedded in the queue file itself, making it self-contained — and as sensitive
as the files it references:

```bash
shadowdrop queue create "https://drop.example.com/d/8UeBnPkose…" \
  --embed-secrets --out queue.json
shadowdrop download --queue queue.json --output-root incoming
```

## Share administration

```bash
shadowdrop share revoke <share-id>   # revoke immediately (admin token required)
shadowdrop share cleanup             # delete blobs of expired/revoked shares
```

The internal share ID is reported as `shareId` in the `--json` output of
`upload` and `share create` — capture it at creation time if you may need to
revoke the share later.
