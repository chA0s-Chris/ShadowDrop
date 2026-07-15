# CLI Guide

The ShadowDrop CLI (`shadowdrop`) encrypts files locally, uploads the
ciphertext, creates shares, and downloads and decrypts shared content. Run
`shadowdrop --help` or `shadowdrop <command> --help` for the full option
reference; this guide covers installation, configuration, and the common
workflows.

## Installation

The supported installers detect the operating system and CPU architecture,
download the matching release binary, verify it against `CHECKSUMS.sha256`,
and replace an existing user-scoped installation.

On Linux and macOS, install the latest stable release to `~/.local/bin`:

```bash
curl -fsSL https://get.shadowdrop.net/install.sh | sh
```

Pass options after `sh -s --` to override the install directory:

```bash
curl -fsSL https://get.shadowdrop.net/install.sh | sh -s -- --install-dir "$HOME/bin"
```

On Windows PowerShell 5.1 or PowerShell 7, install the latest stable release to
`$env:LOCALAPPDATA\ShadowDrop\bin`:

```powershell
iwr -useb https://get.shadowdrop.net/install.ps1 | iex
```

Plain `iwr ... | iex` cannot pass parameters. Use scriptblock invocation for a
custom install directory:

```powershell
& ([scriptblock]::Create((iwr -useb https://get.shadowdrop.net/install.ps1))) -InstallDir "$env:USERPROFILE\Tools\ShadowDrop"
```

The installers warn if the destination is absent from the current `PATH`. Add
the Unix default for the current shell with
`export PATH="$HOME/.local/bin:$PATH"`, or the Windows default with
`$env:PATH = "$env:LOCALAPPDATA\ShadowDrop\bin;$env:PATH"`.

For manual installation, download the binary for your platform from the
[GitHub releases](https://github.com/chA0s-Chris/ShadowDrop/releases). Each
release ships single-file native binaries named
`shadowdrop-<version>-<platform>` for `linux-x64`, `linux-arm64`,
`osx-x64`, `osx-arm64`, `win-x64`, and `win-arm64` (Windows binaries end in
`.exe`), plus a `CHECKSUMS.sha256` file. Verify the exact binary against its
manifest entry before installing it as `shadowdrop` or `shadowdrop.exe`:

```bash
ASSET=shadowdrop-<version>-linux-x64
grep "  ${ASSET}$" CHECKSUMS.sha256 | sha256sum -c -
```

On macOS, use the matching `osx-*` asset and replace `sha256sum` with
`shasum -a 256`. On Windows, compare the manifest's exact asset entry with:

```powershell
# Confirm the hash matches the CHECKSUMS.sha256 entry for this file:
(Get-FileHash "shadowdrop-<version>-win-x64.exe" -Algorithm SHA256).Hash.ToLower()
```

## Updating the CLI

`shadowdrop update` queries the official
[GitHub releases](https://github.com/chA0s-Chris/ShadowDrop/releases) for the
latest stable release (drafts and prereleases are never advertised), compares
it with the installed version, and exits zero after a successful check:

```bash
shadowdrop update
# installed-version:1.4.0
# latest-version:1.5.0
# update-status:update-available
# update-command:curl -fsSL https://get.shadowdrop.net/install.sh | sh -s -- --install-dir '/home/alice/.local/bin'
```

When a newer release exists, the output includes the official installer
invocation for the current platform (`install.ps1` on Windows, `install.sh`
on Linux/macOS — the same scripts as in [Installation](#installation)). The
printed command pins the installer's `--install-dir`/`-InstallDir` to the
directory of the currently running binary, so installations that chose a
custom install directory are updated in place rather than gaining a second
copy in the default location. The CLI never downloads, executes, or replaces
its own binary; run the printed command yourself to update. If the release
lookup times out, fails, or returns malformed data, a diagnostic is written
to stderr and the command exits nonzero.

### Automatic update notifications

After an ordinary command completes in an interactive terminal, the CLI may
print a one-line notice on stderr pointing to `shadowdrop update` when a newer
stable release is known. This is designed to stay out of the way:

- The release source (`api.github.com`) is contacted at most **once per 24
  hours**; results — including failed attempts — are cached in
  `$XDG_CACHE_HOME/shadowdrop` (fallback `~/.cache/shadowdrop`) on Linux/macOS
  and `%LOCALAPPDATA%\ShadowDrop\Cache` on Windows. No data beyond the
  standard HTTP request metadata is sent.
- Checks use a short timeout, are silent on any network or release-service
  failure, and never change the invoked command's exit code.
- Checks are suppressed when stdout or stderr is redirected, in CI
  environments (`CI`, `GITHUB_ACTIONS`), for `--json` invocations, for
  `--help`/`--version`, and for the `update` command itself.
- Set `SHADOWDROP_NO_UPDATE_CHECK=1` to disable automatic checks entirely;
  an explicit `shadowdrop update` still works.

## Commands

| Command                        | Purpose                                                        |
| ------------------------------ | -------------------------------------------------------------- |
| `upload [files]`               | Encrypt, upload, and create a share in one step (files are optional only with `--interactive`). |
| `upload raw <files>`           | Encrypt and upload only; prints file IDs and the share key.    |
| `share create <file-ids>`      | Create a share from previously uploaded file IDs.              |
| `share revoke <share-id>`      | Revoke a share by internal share ID.                           |
| `share cleanup`                | Delete server blobs for expired and revoked shares.            |
| `token create`                 | Create a scoped upload credential and display its token once.  |
| `token list`                   | List bounded upload-credential lifecycle metadata.             |
| `token inspect <credential-id>` | Inspect one upload credential by management ID.               |
| `token revoke <credential-id>` | Revoke one upload credential by management ID.                 |
| `queue create [share-token]`   | Write a download queue file for an existing share.             |
| `download [share-token]`       | Download and decrypt a shared file to disk (or `--queue <file>`). |
| `update`                       | Check whether a newer stable release is available; see [Updating the CLI](#updating-the-cli). |

## Configuration

The server URL and routine upload token are resolved from three sources,
highest precedence first:

1. Command-line flags: `--server-url`, `--upload-token`
2. Environment variables: `SHADOWDROP_SERVER_URL`, `SHADOWDROP_UPLOAD_TOKEN`
3. Config file: `~/.config/shadowdrop/config.json`

```json
{
  "serverUrl": "https://drop.example.com",
  "uploadToken": "sdu1.…",
  "adminToken": "use-a-different-long-random-secret"
}
```

`uploadToken` should normally be the scoped token returned once by
`shadowdrop token create`. It authorizes `upload`, `upload raw`, and
`share create` through `/api/uploads/*` and `/api/shares`; it does not authorize
administrative routes. The bootstrap admin token is also accepted on those
scoped routes for migration and recovery.

Administrative commands (`token create/list/inspect/revoke`, `share revoke`,
and `share cleanup`) resolve a separate token with this precedence:

1. Command-line flag: `--admin-token`
2. Environment variable: `SHADOWDROP_ADMIN_TOKEN`
3. Config-file value: `adminToken`

There is deliberately no fallback from the admin-token setting to
`--upload-token`, `SHADOWDROP_UPLOAD_TOKEN`, or `uploadToken`. Prefer protected
environment variables, secret injection, or a mode-restricted config file over
either token flag because command lines can be visible to process inspection
tools. See [Deployment Hardening](DEPLOYMENT_HARDENING.md) before exposing the
administrative boundary.

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
export SHADOWDROP_UPLOAD_TOKEN="sdu1.…"
```

## Provisioning upload credentials

Set the dedicated admin token, then create a named credential. Names are 1 to
100 characters and, together with credential IDs, are administrative metadata
rather than secrets:

```bash
export SHADOWDROP_ADMIN_TOKEN="use-a-long-random-secret"
shadowdrop token create --name "nightly-backup" \
  --expires-in 90d --max-file-bytes 1073741824 --max-share-bytes 2147483648
# credential-id:4bd9…
# token:sdu1.…
# stderr: Store the token now: it is displayed once and cannot be recovered.
```

The token is shown only by the successful creation response. ShadowDrop never
stores plaintext upload tokens, and list/inspect cannot recover one. Move it
directly into the uploader's secret store and avoid terminal capture, shell
history, logs, support tickets, and shared configuration files.

Credential inventory and lifecycle operations are bounded and scriptable:

```bash
shadowdrop token list --limit 50
shadowdrop token list --cursor <next-cursor> --limit 50
shadowdrop token inspect <credential-id>
shadowdrop token revoke <credential-id>
```

List and inspect expose only management ID, name, timestamps, the fixed
`upload-and-share` capability, and configured byte constraints. `--json` emits
exactly one JSON value on stdout for every token command; the creation token is
inside that value, while banners, warnings, and diagnostics remain on stderr.

Revocation and expiration block every new authenticated upload/share operation
but do not delete uploaded data or revoke shares that were already created.
Each scoped credential can inspect, upload against, and share only its own
reservations/files. The bootstrap admin can use scoped routes with both legacy
ownerless and credential-owned records; a scoped credential can never claim an
ownerless or foreign record.

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

### Upload size limits and batch behavior

Before any file content is sent, `upload` and `upload raw` fetch the effective
encrypted-file limit: the smaller of the server's limit derived from
`Upload:MaxBytes` and the credential's optional `--max-file-bytes`. The client
validates the whole batch, while the server separately enforces both that file
limit and the whole multipart-body limit. A credential's optional
`--max-share-bytes` caps the sum of immutable encrypted lengths in each share;
omitting it means no aggregate-share cap. Request-count and consumable
byte-budget quotas are not implemented.

If any selected file would exceed the effective limit, the command reports
every oversized file with its computed upload size and the maximum, uploads
nothing, and exits non-zero. Resolving the limit is mandatory: against a server
that does not expose it, every upload fails before transfer. If an upload fails
mid-batch, remaining files are not attempted and the command exits non-zero.

## Downloading

### Separate-key download

The recipient passes the share URL (or bare share token plus `--server-url`)
and the key. The decrypted file is written to `./<original-filename>`:

```bash
shadowdrop download "https://drop.example.com/d/qHxI_…" \
  --share-key 5f4a5a7048d41e66dd2833126184beefa46ecf4e9c3c49091a1aafb2e7acfa78
# writes ./report.pdf
```

Use `--out` to choose the destination:

```bash
# explicit file path (parent directories are created as needed)
shadowdrop download "https://drop.example.com/d/qHxI_…" --share-key 5f4a… --out incoming/renamed.pdf

# directory destination — an existing directory, or any value ending in a separator
shadowdrop download "https://drop.example.com/d/qHxI_…" --share-key 5f4a… --out incoming/
# writes ./incoming/report.pdf
```

A value that neither ends in a separator nor names an existing directory is
taken as a file path. Absolute paths and `..` segments in `--out` are honored
as written; the share's announced filename, by contrast, is reduced to a safe
leaf name and can never introduce directories of its own.

If the destination file already exists and matches the shared file, the command
reports it as already downloaded and exits zero; if it exists but differs, the
command fails and leaves the file untouched. Interrupted downloads resume from
the `.partial` file left next to the destination.

`--share-key-file <path>` reads the key from a file instead. If the share
holds multiple files, pick one with `--file <file-id>` — or use a queue to
download all of them to disk. If the share was created with
`--download-token`, the recipient must also pass `--bearer-token <token>`
(this value is only ever taken from the command line).

### Download output streams

The decorated startup banner is written once to **stderr** before executable command work begins; use
`--no-banner` to suppress it. Download progress, `START`/`SUCCESS` lines, and the final `SUMMARY` are written
to **stdout**, for both single-file and queue downloads and in both plain-text
and rich terminal modes. Errors, per-item `FAILED` lines, interactive prompts,
and the guided download summary also go to **stderr**.

> **Breaking change:** download progress and status output moved from stderr to
> stdout. Scripts that captured stderr to read progress, or that relied on
> stdout carrying the decrypted bytes, must be updated. `download` no longer
> writes file content to stdout at all — use the default output path or `--out`.

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
shadowdrop share revoke <share-id>   # revoke immediately
shadowdrop share cleanup             # delete blobs of expired/revoked shares
```

Both commands require the dedicated admin-token configuration described above;
they never use the upload token.

The internal share ID is reported as `shareId` in the `--json` output of
`upload` and `share create` — capture it at creation time if you may need to
revoke the share later.

## Pre-v1 route migration

Scoped uploads are a deliberate pre-v1 breaking change. Routine clients now
use `GET /api/uploads/capabilities`, `POST /api/uploads/reservations`,
`POST /api/uploads`, `GET /api/uploads/{fileId}`, and `POST /api/shares`.
The former upload/share-creation operations under `/api/admin/uploads` and
`POST /api/admin/shares` were removed without compatibility aliases. Update
direct API scripts to the scoped routes, provision an upload credential, keep
the existing `--upload-token`/`SHADOWDROP_UPLOAD_TOKEN`/`uploadToken` setting
for it, and move administrative scripts to the new admin-token setting.
