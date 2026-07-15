<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="images/sd-dark.svg" />
    <img alt="ShadowDrop" src="images/sd-light.svg" width="350" />
  </picture>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/github/license/chA0s-Chris/ShadowDrop?style=for-the-badge" /></a>
  <a href="https://github.com/chA0s-Chris/ShadowDrop/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/chA0s-Chris/ShadowDrop?style=for-the-badge" /></a>
  <a href="https://github.com/chA0s-Chris/ShadowDrop/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/chA0s-Chris/ShadowDrop/total?style=for-the-badge&label=Downloads&color=blue"></a>
  <a href="https://hub.docker.com/r/chaos/shadowdrop"><img alt="Docker pulls" src="https://img.shields.io/docker/pulls/chaos/shadowdrop?style=for-the-badge" /></a>
</p>

ShadowDrop is a self-hosted service for secure one-off file handoffs. The CLI
encrypts files on the sender's machine (AES-256-GCM), and default CLI
downloads keep decryption on the recipient's side so the server stores and
serves only ciphertext without seeing key material. Direct-HTTP downloads trade
that property for browser/curl compatibility by sending key material to the
server for server-side decryption.

A typical handoff: an operator runs the ShadowDrop container, uploads files
with the CLI, sends the recipient the share URL, and delivers the decryption
key over a separate channel. Shares expire automatically (default: 7 days) and
can be revoked at any time.

ShadowDrop ships through two channels:

- **API server** — Docker image [`chaos/shadowdrop`](https://hub.docker.com/r/chaos/shadowdrop)
  on Docker Hub (the only image registry).
- **CLI** — single-file native binaries for Linux, macOS, and Windows from the
  [GitHub releases](https://github.com/chA0s-Chris/ShadowDrop/releases).

## Quick start: run the server

Clone the repository, create the credential file, and start the default LiteDB
metadata + filesystem blob deployment:

```bash
cp docker/.env.example docker/.env
# Set a long, random SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN in docker/.env.
docker compose --env-file docker/.env -f docker/compose.local.yaml up -d
```

The alternative `docker/compose.mongodb.yaml` runs ShadowDrop with MongoDB metadata
and GridFS blobs. It requires the additional MongoDB values documented in the
[deployment guide](docs/DEPLOYMENT.md#docker-compose-deployments). Both Compose
files bind the API to `127.0.0.1:19423` by default and persist their state in
named volumes.

Running the published image directly remains supported:

```bash
docker run -d --name shadowdrop \
  -p 19423:19423 \
  -v shadowdrop-data:/app/data \
  -e SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN="use-a-long-random-secret" \
  chaos/shadowdrop:latest
```

The container listens on plain HTTP port `19423` and expects TLS to be
terminated by a reverse proxy in front of it. All state (metadata database and
encrypted blobs) lives under `/app/data` — keep it on a persistent volume.
`SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` is required on the first start and becomes
the admin bearer token; see the [deployment guide](docs/DEPLOYMENT.md) for
details, both Compose options, the Docker Hub tagging scheme, backup/restore,
and reverse-proxy guidance.

Do not expose `/api/admin/*` to the public Internet without an upstream
control — read [deployment hardening](docs/DEPLOYMENT_HARDENING.md) before going live.

## Quick start: CLI

On Linux and macOS, install the latest stable CLI to `~/.local/bin`:

```bash
curl -fsSL https://get.shadowdrop.net/install.sh | sh
```

On Windows, install it to `$env:LOCALAPPDATA\ShadowDrop\bin`:

```powershell
iwr -useb https://get.shadowdrop.net/install.ps1 | iex
```

Both installers detect the platform, verify the selected binary against the
release's `CHECKSUMS.sha256`, replace an existing installation, and warn when
the target directory is not on `PATH`. Override the install directory with:

```bash
curl -fsSL https://get.shadowdrop.net/install.sh | sh -s -- --install-dir "$HOME/bin"
```

```powershell
& ([scriptblock]::Create((iwr -useb https://get.shadowdrop.net/install.ps1))) -InstallDir "$env:USERPROFILE\Tools\ShadowDrop"
```

See the [CLI installation guide](docs/CLI.md#installation) for supported
platforms, manual checksum verification, and `PATH` guidance.

Point the CLI at your server and use the bootstrap admin token once to create a
scoped upload credential. Its plaintext is displayed only by `token create` and
cannot be recovered later, so capture it immediately:

```bash
export SHADOWDROP_SERVER_URL="https://drop.example.com"
export SHADOWDROP_ADMIN_TOKEN="use-a-long-random-secret"
shadowdrop token create --name "alice-laptop"
# credential-id:…
# token:sdu1.…

export SHADOWDROP_UPLOAD_TOKEN="sdu1.…"
unset SHADOWDROP_ADMIN_TOKEN
```

The upload credential can upload encrypted files and create shares, but cannot
revoke other shares, run cleanup, or manage credentials. The bootstrap admin
token remains valid on the scoped upload routes for migration and recovery;
routine clients should use scoped credentials instead. See
[security trade-offs](docs/SECURITY_TRADEOFFS.md) for the trust boundaries.

Upload a file — the CLI encrypts it locally and prints a share URL plus the
decryption key:

```bash
shadowdrop upload report.pdf
# share-url:https://drop.example.com/d/qHxI_3N1cTzPNkt1WSi2rieBiSi858y-OA1Sc_OQlz4
# share-key:5f4a5a7048d41e66dd2833126184beefa46ecf4e9c3c49091a1aafb2e7acfa78
```

Send the recipient the share URL, and the share key over a **different**
channel. The recipient downloads and decrypts with the CLI; the file lands in
the current directory under its original name:

```bash
shadowdrop download "https://drop.example.com/d/qHxI_3N1cTzPNkt1WSi2rieBiSi858y-OA1Sc_OQlz4" \
  --share-key 5f4a5a7048d41e66dd2833126184beefa46ecf4e9c3c49091a1aafb2e7acfa78
# writes ./report.pdf
```

Pass `--out <file>` for an explicit destination, or `--out <directory>` to keep
the original name inside a directory of your choosing.

For recipients without the CLI there is a direct-HTTP mode (`--direct-http`)
that emits a browser-compatible URL and a `curl` command — with weaker
secrecy properties. The [CLI guide](docs/CLI.md) covers all commands,
configuration sources, download queues, and credential-handling options.

## Documentation

- [HTTP API](docs/API.md) — every endpoint with its route, method, purpose,
  audience, and required authentication.
- [Deployment guide](docs/DEPLOYMENT.md) — container deployment, `/app/data`
  persistence, Docker Hub tags, reverse-proxy TLS and public hostnames.
- [CLI guide](docs/CLI.md) — installation, configuration precedence, and
  copy-pasteable examples for every workflow.
- [Security trade-offs](docs/SECURITY_TRADEOFFS.md) — separate-key versus
  direct-HTTP shares, key-leakage channels, bearer tokens, TLS trust options.
- [Deployment hardening](docs/DEPLOYMENT_HARDENING.md) — admin endpoint
  exposure, reverse-proxy controls, direct-HTTP URL sensitivity.

## Current MVP limitations

- Every scoped upload credential has one fixed `upload-and-share` capability;
  independently assignable permissions and request-count or byte-budget usage
  quotas are not part of this release.
- There is no web UI; shares are consumed via the CLI or direct HTTP.

## License

ShadowDrop is licensed under the [MIT license](LICENSE).
