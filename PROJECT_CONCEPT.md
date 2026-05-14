# ShadowDrop Project Concept

## Summary

ShadowDrop is a lightweight, self-hosted secure file handoff service.

It is intended for people who occasionally need to share sensitive files with trusted recipients without placing those files, unencrypted, in a public cloud drive or operating a heavy collaboration platform.

The core experience should be simple:

1. An authorized sender uploads a file.
2. The sender client encrypts the file during upload.
3. The sender creates a temporary download link.
4. The recipient downloads the file over standard HTTP tooling such as `curl`, a browser, or a small optional CLI.

ShadowDrop is not meant to be a cloud drive, sync tool, document workspace, or replacement for platforms like NextCloud. It should stay focused on secure, temporary file transfer.

## Problem

Sharing sensitive files with friends or trusted contacts is often more awkward than it should be.

Common options have tradeoffs:

- Public cloud drives are convenient, but may be inappropriate for sensitive files.
- Manual encryption before upload is safer, but cumbersome and error-prone.
- FTP/SFTP servers work, but require more recipient-side setup and account management.
- Full collaboration suites are powerful, but heavy for simple file handoff.

For users with private hosting infrastructure, there is room for a small purpose-built service that handles encrypted storage and controlled HTTP-based retrieval.

## Target Users

The initial target users are technically comfortable individuals who run personal infrastructure, such as a home lab or small private VPS.

Examples:

- A person sharing private documents with friends or family.
- A home lab user who wants a controlled alternative to public cloud links.
- A small trusted group that needs occasional secure file handoff without shared accounts.
- A developer or operator who wants scriptable upload and download workflows.

## Product Principles

- **Self-hosted first:** The operator controls the server, database, and storage backend.
- **One-container default:** The default distribution should work as a Docker image without requiring an external database service.
- **Cross-platform distribution:** The CLI and Docker image should support both x64 and arm64 targets.
- **Small and focused:** Avoid cloud-drive, sync, collaboration, and account-heavy features.
- **HTTP-native downloads:** Recipients should be able to retrieve files with standard tools.
- **Always-resumable downloads:** Downloads must support resuming after interruptions instead of forcing recipients to restart from the beginning.
- **Client-side upload encryption:** Files should be encrypted by the sending client before being written to the storage backend.
- **Temporary access:** Shares should expire by time or explicit revocation. Access-count-based limits can be added later.
- **Token-only authorization:** Access should be controlled with bearer tokens, without introducing users, accounts, or identity management.
- **Separable API exposure:** Download endpoints and upload/management endpoints should be independently exposable so operators can keep management APIs off the public internet.
- **Automation-friendly:** Uploading, sharing, and downloading should work from scripts and CLI tools.
- **Guided when useful:** The CLI should support an interactive terminal UI for users who prefer a wizard-like flow.
- **Backend-flexible:** Blob storage and metadata persistence should both be abstracted so different backends can be supported over time.
- **Vertical-slice architecture:** Features should be organized around workflows and use cases, not split into broad Clean Architecture-style projects.

## MVP Scope

The first version should focus on a narrow but complete handoff flow.

Included:

- Authenticated file upload for senders.
- Client-side encryption during upload.
- Local filesystem storage backend.
- LiteDB metadata database backend.
- Docker image distribution with a one-container default deployment for x64 and arm64.
- Admin bearer token required for uploads and share management.
- Bootstrap admin token through `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`.
- Configuration to limit exposure of upload and management APIs separately from public download endpoints.
- Password-protected links using optional download bearer tokens.
- Creation of opaque, high-entropy download links.
- Shares containing one or more files.
- Individually addressable files within a multi-file share.
- Download links that can optionally include the decryption key for direct HTTP retrieval.
- CLI-assisted downloads when the decryption key is supplied separately.
- Explicit opt-in direct HTTP mode for browser or plain `curl` downloads where the server receives the decryption key during download.
- Download links with expiration timestamp.
- Time-based expiration for shares and download bearer tokens.
- Share-scoped retention where expiring or revoking a share removes the share and its files, or marks them for cleanup.
- Original file names exposed by default, with uploader-controlled display name overrides.
- Revocation or deletion of shares.
- HTTP `GET` download endpoint usable by `curl`.
- Resumable downloads using HTTP range requests.
- Scriptable CLI where every operation can be completed through parameters and flags.
- CLI command parsing based on System.CommandLine.
- Interactive CLI built with Spectre.Console, including cursor-capable terminal UI and wizard-like flows.
- Native AOT-compatible CLI implementation and dependencies.
- CLI binaries for Linux, macOS, and Windows on x64 and arm64.
- CLI configuration file in the user's home directory for settings such as server URL and admin token.

Deferred:

- S3-compatible storage.
- MongoDB metadata database backend.
- SQLite metadata database backend.
- Access-count-based share or token limits.
- Full end-to-end browser experience without the CLI.

## Core Workflows

### Upload a File

An admin-token-authorized sender uploads a file through the API or CLI.

The sender client encrypts the file before or during upload. ShadowDrop records metadata and stores the encrypted blob in the configured storage backend.

Example CLI direction:

```sh
shadowdrop upload ./document.pdf --admin-token <token>
```

### Create a Share Link

The sender creates a share for one or more uploaded files with access constraints.

Example CLI direction:

```sh
shadowdrop share <file-id> <file-id> --expires 24h --admin-token <token>
```

The output is a bearer-style URL that can be sent to the recipient.

A share should support multiple files from the start. Each file in the share must remain individually addressable so recipients can download only the file they need instead of being forced to download an archive or the entire share.

Files should not have independent retention policies in the MVP. Share expiration and revocation are the lifecycle boundaries: when a share expires or is revoked, ShadowDrop should remove the share and all files associated with it, or mark them for cleanup by housekeeping.

Download links should expose original file names by default. The uploader should be able to override the exposed file names when creating or updating the share.

The share can be created in two modes:

- **Direct HTTP mode:** The download request includes enough key material for the server to decrypt while serving the response. This mode must be explicit opt-in per share because the server receives the decryption key during download.
- **CLI decrypt mode:** The download URL does not include the decryption key, and the recipient provides the key to the CLI so decryption happens client-side after download. This should be the default sharing mode.

Shares can also be password-protected with an optional download bearer token. If a share requires a download token, the recipient must provide it in addition to the share token and any required decryption key.

A plain browser download can include decryption key material in the URL query string, but it cannot attach an `Authorization: Bearer ...` header by itself. ShadowDrop should not support download bearer tokens in query parameters. Bearer-token-protected downloads are therefore CLI/API-only. If a recipient can only use a browser, the share should not require a download bearer token.

### Download a File

The recipient retrieves the file with standard HTTP tooling.

Example:

```sh
curl -L -o document.pdf "https://drop.example.com/d/<token>"
```

In direct HTTP mode, ShadowDrop validates the share token, checks expiration and access limits, validates any required download bearer token, uses key material from the request to decrypt the file, streams it to the recipient, and updates access metadata.

Downloads must always be resumable. ShadowDrop should support HTTP range requests so interrupted downloads can continue from the last received byte with standard clients such as `curl -C -`. To make this work with encrypted files, file content must be encrypted in independently decryptable chunks.

For command-line clients, key material can be provided through the `ShadowDrop-Key` HTTP header so it is not part of the URL:

```sh
curl -L \
  -H "ShadowDrop-Key: <key>" \
  -H "Authorization: Bearer <download-token>" \
  -o document.pdf \
  "https://drop.example.com/d/<token>"
```

For browser downloads, direct HTTP mode should also support key material in the `sd-key` query parameter:

```text
https://drop.example.com/d/<token>?sd-key=<key>
```

In CLI decrypt mode, ShadowDrop validates the token, checks expiration and access limits, streams the encrypted file, and the recipient CLI decrypts it locally using a separately provided key.

For multi-file shares, the CLI should be able to download one file, selected files, or all files in a share. A queue file format should describe the files in a share so the CLI can manage batch downloads.

### Revoke a Share

The sender can revoke a share before it naturally expires. Revoking a share should block further access and remove the associated files, or mark them for cleanup by a housekeeping process.

Example CLI direction:

```sh
shadowdrop revoke <share-id> --admin-token <token>
```

## Token Model

ShadowDrop should use bearer tokens for authorization, without user accounts, profiles, roles, or identity management.

There are two token categories:

- **Admin tokens:** Required for sender-side operations such as uploading files, creating shares, revoking shares, deleting files, and managing tokens.
- **Download tokens:** Optional share-scoped tokens that can be required before a download is allowed. These are the MVP mechanism for password-protected links.

Tokens should be stored by the metadata database backend. Stored token material should be protected, preferably by storing only a cryptographic hash of the token rather than the plaintext token value.

Admin tokens are deployment-level credentials. The system may support one or more active admin tokens so an operator can rotate credentials without downtime.

The MVP should support first-run admin token bootstrapping through the `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` environment variable. This keeps Docker and Portainer-style deployments simple. ShadowDrop should validate admin API requests through `Authorization: Bearer <admin-token>`.

When a bootstrap admin token is provided, ShadowDrop should store only protected token material, such as a cryptographic hash, in the metadata database. It should not persist the plaintext token. Exact rotation and bootstrap re-run semantics can be refined during implementation.

Download tokens are share-level access gates. They may have their own expiration timestamp, independent of the share expiration timestamp. This allows a share link to remain structurally valid while a separate download credential expires earlier.

The MVP should use time-based expiration for download bearer tokens. Access-count-based token limits can be added later.

For CLI and API access, download bearer tokens should use the standard `Authorization: Bearer <token>` header. Browser-compatible downloads should not support bearer tokens because putting access tokens into URLs increases exposure through browser history, logs, referrers, screenshots, and copied links without meaningfully improving the browser-only recipient experience.

Token authorization should stay deliberately simpler than user management:

- No users.
- No profiles.
- No passwords for accounts.
- No registration.
- No login sessions.
- No ownership model beyond what is encoded in stored file, share, and token records.

## CLI Experience

The ShadowDrop CLI should be both automation-friendly and comfortable for manual use.

The CLI should be distributed for Linux, macOS, and Windows, with x64 and arm64 builds for each platform.

Every operation should be available through explicit commands, parameters, and flags so the CLI works well in scripts, CI jobs, aliases, and shell history.

CLI command parsing should use System.CommandLine. Spectre.Console should be used for terminal rendering, prompts, tables, progress, markup, and interactive UI behavior. Spectre.Console.Cli is not required for the planned command model.

The same CLI should also offer an interactive terminal UI built with Spectre.Console. The interactive mode should use cursor-capable prompts and wizard-like flows for tasks such as uploading a file, choosing share constraints, deciding whether direct HTTP mode is allowed, adding an optional download bearer token, and displaying the resulting link and key material.

The interactive mode should improve ergonomics without becoming the only way to perform an operation. Any action available through the terminal UI should also be possible non-interactively.

The CLI should support multi-file share workflows. It should be able to upload and share multiple files, inspect a share, download individual files from a share, and process a queue file for downloading multiple files.

Initial command shape:

```sh
shadowdrop upload ./file.pdf
shadowdrop upload ./file.pdf ./other.txt
shadowdrop share create <file-id> <file-id> --expires 24h
shadowdrop put ./file.pdf ./other.txt --expires 24h
shadowdrop put ./file.pdf --expires 24h --direct-http
shadowdrop put ./file.pdf --expires 24h --download-token <token>
shadowdrop put ./secret.pdf --name "documents.pdf"
shadowdrop download <share-id>
shadowdrop download <share-id> --file <file-id>
shadowdrop download --queue share.queue.json
shadowdrop share inspect <share-id>
shadowdrop share revoke <share-id>
shadowdrop config set serverUrl https://drop.example.com
shadowdrop config set adminToken <token>
```

`put` is the convenience command for uploading files and creating a share in one step. `upload` and `share create` remain available for explicit two-step workflows. Queue-based downloads should not require a separate share id argument because the queue file already contains the target server and share id.

The CLI should support a JSON configuration file at `~/.config/shadowdrop/config.json`. The config file can store defaults such as the ShadowDrop server URL, admin token, preferred output format, and future CLI settings.

CLI parameters and environment variables should be able to override config-file values for automation and temporary use.

Because the admin token grants upload and share-management access, the CLI should treat the config file as sensitive. The MVP should document file permission expectations and avoid printing stored tokens back to the terminal.

Everything related to the CLI should be compatible with Native AOT. CLI code and dependencies should avoid runtime code generation, dynamic assembly loading, and reflection patterns that are unsafe under trimming or Native AOT. Native AOT publish checks should be part of validation for every supported CLI runtime identifier.

## Security Model

Initial design should treat download links as bearer capabilities. Anyone with a valid link can download the associated file until the access policy blocks it.

The MVP should use strong random tokens with enough entropy to make guessing impractical. This applies to share tokens, admin bearer tokens, and optional download bearer tokens.

The initial encryption model should use client-side encryption during upload:

- Generate one random share encryption secret on the sending client.
- Derive per-file AES-256-GCM content keys from the share encryption secret and file-specific context.
- Encrypt file content before or while uploading it.
- Use AES-256-GCM as the MVP encryption algorithm.
- Encrypt files in fixed-size chunks so range requests can be served without decrypting the entire file.
- Use a unique nonce per chunk and authenticate chunk metadata such as encryption version, file id, chunk index, and plaintext chunk length.
- Store an encryption format version and algorithm identifier with each encrypted file so additional formats can be supported later.
- Store only encrypted blobs in the storage backend.
- Do not require the server to persist plaintext share secrets or derived file keys.
- Allow the sender to choose whether the share encryption secret is embedded in the download link or delivered separately.

This protects against exposure of the storage backend contents and reduces the amount of secret material the server needs to store.

There are two important download modes:

- **Link-carried key mode:** The request includes the share encryption secret or enough key material to derive it. This supports ordinary HTTP clients such as `curl` and browser downloads, but the server receives the key material during download if it must decrypt the response. This mode must be explicit opt-in per share.
- **Separate-key mode:** The share link does not include the share encryption secret. The recipient receives the secret through a separate channel and uses the CLI to decrypt locally. This avoids exposing the secret to the server during download, but requires recipient tooling. This should be the default mode because tech-oriented users are expected to prefer the CLI, while browser or plain `curl` sharing is a compatibility choice for recipients who need it.

If the server is expected to decrypt and stream plaintext to `curl`, the key cannot live only in a URL fragment because fragments are not sent in HTTP requests. Direct HTTP mode should support key material in the `ShadowDrop-Key` HTTP header for CLI and script usage, and in the `sd-key` query parameter for browser download support. Query parameters have stronger logging and sharing exposure risks, so this mode should be documented clearly and handled carefully.

The same URL-exposure concern applies more strongly to optional download bearer tokens. ShadowDrop should not accept bearer tokens through query parameters. Token-protected downloads require a client that can send headers, such as `curl` or the ShadowDrop CLI.

Future designs may explore stronger or more specialized models:

- Browser-side decryption.
- Public-key recipient encryption.
- Separate key storage services.
- Recipient passphrases.
- Hardware-backed server keys.

## Architecture Direction

The codebase should prefer vertical slices over a multi-project Clean Architecture layout. The main application can still have clear internal boundaries, but features should be organized around workflows such as upload, share creation, download, revocation, and retention.

Each slice should own the endpoint, request and response contracts, validation, use-case logic, and tests for that workflow. Shared infrastructure should exist only where it removes real duplication or represents a true cross-cutting concern.

The system still needs a few clear capabilities:

- **API service:** Upload, share creation, revocation, and download endpoints.
- **API exposure configuration:** Independent exposure controls for public download endpoints and protected upload/management endpoints.
- **Blob storage abstraction:** Local filesystem first, S3-compatible later.
- **Metadata persistence abstraction:** LiteDB first, MongoDB or SQLite later.
- **Crypto capability:** Chunked AES-256-GCM MVP encryption, versioned encryption formats for future algorithms, client-side upload encryption, server-side direct-download decryption when key material is supplied, and CLI-side recipient decryption.
- **Share service:** Share token generation, time-based expiration, file display names, and revocation.
- **Token capability:** Admin token validation, optional download token validation, token expiration, token rotation, and hashed token storage.
- **Metadata repository:** File records, share records, token records, storage locations, access state, and audit metadata.
- **CLI:** Native AOT-compatible command-line tool using System.CommandLine for command parsing and Spectre.Console for interactive terminal UI workflows.

Potential download endpoint shape:

```http
GET /d/{token}
GET /d/{token}/files/{fileId}
```

Potential API endpoint direction:

```http
POST /api/files
POST /api/files/{fileId}/shares
DELETE /api/shares/{shareId}
```

The upload and management API should be configurable separately from the download API. Many deployments should expose only download endpoints publicly, while upload and management endpoints remain available only on a private network, VPN, reverse-proxy route, internal listener, or disabled public listener. Admin bearer tokens are still required, but network exposure should be an additional deployment control.

Potential CLI/API direct-download key transport:

```http
GET /d/{token}
ShadowDrop-Key: <key>
Authorization: Bearer <download-token>
```

Potential browser-compatible direct-download key transport:

```http
GET /d/{token}?sd-key=<key>
```

## Persistence Backends

ShadowDrop needs two persistence layers:

- A blob storage backend for encrypted file content.
- A metadata database backend for file records, share records, token records, access state, storage references, and audit metadata.

The first blob storage backend should be a local filesystem backend because it is simple, testable, and useful for home lab deployments.

Later blob storage backends could include:

- S3-compatible object storage.
- MinIO.
- Azure Blob Storage.
- Other object-store-style providers.

The first metadata database backend should be LiteDB. This keeps the default deployment simple and supports a one-container Docker distribution without requiring users to run a separate database service.

Later metadata database backends could include:

- MongoDB.
- SQLite.
- Other embedded or server-backed databases if they fit the repository abstraction.

The storage and database abstractions should avoid leaking backend-specific behavior into higher-level services.

## Metadata Model

ShadowDrop should keep operational and audit metadata intentionally minimal.

Share metadata should include:

- Share id.
- Hashed share token.
- Creation timestamp.
- Expiration timestamp.
- Revocation timestamp, if revoked.
- Cleanup state, such as active, expired, revoked, cleanup-pending, or cleaned.
- Whether direct HTTP mode is enabled.
- Whether a download bearer token is required.
- File entries included in the share.

File metadata should include:

- File id.
- Share id.
- Storage object or blob path.
- Original file name.
- Exposed or display file name.
- Plaintext size.
- Plaintext SHA-256 hash, if provided by the uploading client.
- Encrypted size.
- Content type, if known.
- Encryption format version.
- Encryption algorithm.
- Chunk size.
- Chunk count.
- Per-chunk metadata needed for range reads, or a reference to that metadata.

Token metadata should include:

- Token id.
- Token type, such as admin or download.
- Hashed token value.
- Share id for download tokens.
- Creation timestamp.
- Expiration timestamp, if any.
- Revocation timestamp, if any.
- Optional label for operator recognition.

Audit metadata should cover events such as:

- Share created.
- Share revoked.
- Share expired.
- File downloaded.
- Failed authorization attempt.
- Cleanup completed.

Audit events should only store:

- Timestamp.
- Event type.
- Share id.
- File id, if relevant.
- Token type used, not the token value.
- Limited client information if useful, such as user agent or remote IP.

ShadowDrop should not store:

- Plaintext encryption keys.
- Plaintext bearer tokens.
- Recipient identity.
- Unnecessary request headers.
- Full URLs containing `sd-key`.
- Query strings.
- File contents or previews.

Plaintext SHA-256 hashes are useful for CLI verification after download and decryption, but they are metadata and can reveal equality with known files. The MVP should support them as optional metadata. If they prove too revealing later, ShadowDrop can make them opt-in, remove them from queue files, or replace them with keyed or encrypted verification metadata.

## Queue File Format

The CLI queue file should be JSON and should not contain plaintext secrets such as admin tokens, download bearer tokens, decryption keys, or `sd-key` values.

Initial queue file shape:

```json
{
  "shadowDrop": "1.0",
  "queueVersion": "1.0",
  "target": "https://example.com",
  "shareId": "abc123",
  "files": [
    {
      "fileId": "1",
      "fileName": "example.txt",
      "length": 4096,
      "plaintextSha256": "hex-encoded-sha256"
    }
  ]
}
```

Fields:

- `shadowDrop`: ShadowDrop product/version marker.
- `queueVersion`: Queue file format version.
- `target`: ShadowDrop server base URL.
- `shareId`: Share identifier.
- `files`: Files available through the share.
- `fileId`: File identifier, represented as a string to allow opaque IDs later.
- `fileName`: Suggested local file name.
- `length`: Plaintext file size in bytes.
- `plaintextSha256`: Optional plaintext SHA-256 hash for verifying the decrypted file.

## Distribution

ShadowDrop should be easy to run and easy to install.

The server should be distributed as a Docker image with a one-container default deployment. The image should support x64 and arm64 so it works on common VPS, home lab servers, NAS devices, and small ARM-based machines.

The CLI should be distributed as native command-line builds for:

- Linux x64.
- Linux arm64.
- macOS x64.
- macOS arm64.
- Windows x64.
- Windows arm64.

Native AOT should be the preferred CLI distribution model. If a platform-specific AOT blocker appears, self-contained single-file publishing can be used only as a fallback while the blocker is addressed.

## Non-Goals

ShadowDrop should not become:

- A cloud drive.
- A general document management system.
- A team collaboration suite.
- A sync client.
- A public anonymous file host.
- A replacement for full encrypted messaging.
- A web UI for recipients.
- A sender dashboard.
- Recipient accounts.
- User management or identity concepts.
- Public user registration.
- Folder synchronization.
- File previews.
- Collaboration features.
- Comments, editing, or version history.

## Initial Acceptance Criteria

- [x] The concept clearly defines ShadowDrop as a self-hosted secure file handoff service.
- [x] The concept identifies the target users and core problem.
- [x] The concept describes the MVP scope and non-goals.
- [x] The concept outlines upload, share, download, and revoke workflows.
- [x] The concept records initial security assumptions and open security questions.
- [x] The concept keeps blob storage backend design pluggable, starting with local filesystem storage.
- [x] The concept keeps metadata database design pluggable, starting with LiteDB.
- [x] The concept prefers vertical slices over a broad Clean Architecture project split.
- [x] The concept describes client-side upload encryption with direct HTTP and CLI decrypt download modes.
- [x] The concept uses bearer tokens without user or identity management.
- [x] The concept supports admin bearer tokens and optional expiring download bearer tokens.
- [x] The concept keeps bearer-token-protected downloads limited to CLI/API clients instead of browser query parameters.
- [x] The concept defines `ShadowDrop-Key` as the decryption key header.
- [x] The concept defines `sd-key` as the browser-compatible decryption key query parameter.
- [x] The concept includes password-protected links using bearer tokens.
- [x] The concept defaults to a one-container Docker distribution without an external database dependency.
- [x] The concept requires downloads to always be resumable through HTTP range requests.
- [x] The concept uses chunked AES-256-GCM as the MVP encryption algorithm with versioned formats for later algorithms.
- [x] The concept makes direct HTTP mode explicit opt-in per share because the server receives the decryption key during download.
- [x] The concept uses Spectre.Console for a CLI that supports both fully parameterized commands and an interactive terminal UI.
- [x] The concept uses System.CommandLine for CLI command parsing.
- [x] The concept requires all CLI code and dependencies to be Native AOT compatible.
- [x] The concept supports a JSON CLI config file at `~/.config/shadowdrop/config.json` for defaults such as server URL and admin token.
- [x] The concept requires CLI distribution for Linux, macOS, and Windows on x64 and arm64.
- [x] The concept requires Docker image distribution for x64 and arm64.
- [x] The concept supports multiple files per share while keeping each file individually addressable.
- [x] The concept defines a JSON queue file format for CLI-managed multi-file downloads.
- [x] The concept supports optional plaintext SHA-256 metadata for post-download CLI verification.
- [x] The concept scopes download bearer tokens to shares.
- [x] The concept starts with time-based expiration for download bearer tokens and defers access-count-based token limits.
- [x] The concept exposes original file names by default while allowing uploader-controlled display name overrides.
- [x] The concept makes separate-key CLI decrypt mode the default, with direct HTTP mode as an explicit compatibility choice.
- [x] The concept ties file retention to share expiration instead of independent file retention policies.
- [x] The concept makes share revocation remove associated files or mark them for cleanup.
- [x] The concept bootstraps admin access through `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`.
- [x] The concept defines a minimal operational and audit metadata model.
- [x] The concept defines the initial CLI command shape, including `put` as the combined upload-and-share command.
- [x] The concept lets operators limit public exposure of upload and management APIs separately from download endpoints.
