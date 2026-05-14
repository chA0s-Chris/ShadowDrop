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
- **Temporary access:** Shares should expire by time, access count, or explicit revocation.
- **Token-only authorization:** Access should be controlled with bearer tokens, without introducing users, accounts, or identity management.
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
- Password-protected links using optional download bearer tokens.
- Creation of opaque, high-entropy download links.
- Shares containing one or more files.
- Individually addressable files within a multi-file share.
- Download links that can optionally include the decryption key for direct HTTP retrieval.
- CLI-assisted downloads when the decryption key is supplied separately.
- Explicit opt-in direct HTTP mode for browser or plain `curl` downloads where the server receives the decryption key during download.
- Download links with expiration timestamp.
- Download links with optional maximum access count.
- Revocation or deletion of shares.
- HTTP `GET` download endpoint usable by `curl`.
- Resumable downloads using HTTP range requests.
- Scriptable CLI where every operation can be completed through parameters and flags.
- Interactive CLI built with Spectre.Console, including cursor-capable terminal UI and wizard-like flows.
- CLI binaries for Linux, macOS, and Windows on x64 and arm64.
- CLI configuration file in the user's home directory for settings such as server URL and admin token.

Deferred:

- Web UI for recipients.
- Full sender dashboard.
- Recipient accounts.
- User management or identity concepts.
- Folder synchronization.
- File previews.
- Collaboration features.
- Comments, editing, or version history.
- Public user registration.
- S3-compatible storage.
- MongoDB metadata database backend.
- SQLite metadata database backend.
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
shadowdrop share <file-id> <file-id> --expires 24h --downloads 3 --admin-token <token>
```

The output is a bearer-style URL that can be sent to the recipient.

A share should support multiple files from the start. Each file in the share must remain individually addressable so recipients can download only the file they need instead of being forced to download an archive or the entire share.

The share can be created in two modes:

- **Direct HTTP mode:** The download request includes enough key material for the server to decrypt while serving the response. This mode must be explicit opt-in per share because the server receives the decryption key during download.
- **CLI decrypt mode:** The download URL does not include the decryption key, and the recipient provides the key to the CLI so decryption happens client-side after download.

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

For multi-file shares, the CLI should be able to download one file, selected files, or all files in a share. A future queue file format can describe multiple files, target paths, share tokens, key material references, and download constraints so the CLI can resume and manage batch downloads.

### Revoke a Share

The sender can revoke a share before it naturally expires.

Example CLI direction:

```sh
shadowdrop revoke <share-id> --admin-token <token>
```

## Token Model

ShadowDrop should use bearer tokens for authorization, without user accounts, profiles, roles, or identity management.

There are two token categories:

- **Admin tokens:** Required for sender-side operations such as uploading files, creating shares, revoking shares, deleting files, and managing tokens.
- **Download tokens:** Optional tokens that can be required by specific files or shares before a download is allowed. These are the MVP mechanism for password-protected links.

Tokens should be stored by the metadata database backend. Stored token material should be protected, preferably by storing only a cryptographic hash of the token rather than the plaintext token value.

Admin tokens are deployment-level credentials. The system may support one or more active admin tokens so an operator can rotate credentials without downtime.

Download tokens are share-level or file-level access gates. They may have their own expiration timestamp, independent of the share expiration timestamp. This allows a share link to remain structurally valid while a separate download credential expires earlier.

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

The same CLI should also offer an interactive terminal UI built with Spectre.Console. The interactive mode should use cursor-capable prompts and wizard-like flows for tasks such as uploading a file, choosing share constraints, deciding whether direct HTTP mode is allowed, adding an optional download bearer token, and displaying the resulting link and key material.

The interactive mode should improve ergonomics without becoming the only way to perform an operation. Any action available through the terminal UI should also be possible non-interactively.

The CLI should support multi-file share workflows. It should be able to upload and share multiple files, inspect a share, download individual files from a share, and process a queue file for downloading multiple files.

The CLI should support a JSON configuration file at `~/.config/shadowdrop/config.json`. The config file can store defaults such as the ShadowDrop server URL, admin token, preferred output format, and future CLI settings.

CLI parameters and environment variables should be able to override config-file values for automation and temporary use.

Because the admin token grants upload and share-management access, the CLI should treat the config file as sensitive. The MVP should document file permission expectations and avoid printing stored tokens back to the terminal.

## Security Model

Initial design should treat download links as bearer capabilities. Anyone with a valid link can download the associated file until the access policy blocks it.

The MVP should use strong random tokens with enough entropy to make guessing impractical. This applies to share tokens, admin bearer tokens, and optional download bearer tokens.

The initial encryption model should use client-side encryption during upload:

- Generate a random data encryption key per file on the sending client.
- Encrypt file content before or while uploading it.
- Use AES-256-GCM as the MVP encryption algorithm.
- Encrypt files in fixed-size chunks so range requests can be served without decrypting the entire file.
- Use a unique nonce per chunk and authenticate chunk metadata such as encryption version, file id, chunk index, and plaintext chunk length.
- Store an encryption format version and algorithm identifier with each encrypted file so additional formats can be supported later.
- Store only encrypted blobs in the storage backend.
- Do not require the server to persist plaintext file keys.
- Allow the sender to choose whether the key is embedded in the download link or delivered separately.

This protects against exposure of the storage backend contents and reduces the amount of secret material the server needs to store.

There are two important download modes:

- **Link-carried key mode:** The request includes the file key or enough key material to derive it. This supports ordinary HTTP clients such as `curl` and browser downloads, but the server receives the key during download if it must decrypt the response. This mode must be explicit opt-in per share.
- **Separate-key mode:** The share link does not include the file key. The recipient receives the key through a separate channel and uses the CLI to decrypt locally. This avoids exposing the key to the server during download, but requires recipient tooling.

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
- **Blob storage abstraction:** Local filesystem first, S3-compatible later.
- **Metadata persistence abstraction:** LiteDB first, MongoDB or SQLite later.
- **Crypto capability:** Chunked AES-256-GCM MVP encryption, versioned encryption formats for future algorithms, client-side upload encryption, server-side direct-download decryption when key material is supplied, and CLI-side recipient decryption.
- **Share service:** Share token generation, expiration, access counting, and revocation.
- **Token capability:** Admin token validation, optional download token validation, token expiration, token rotation, and hashed token storage.
- **Metadata repository:** File records, share records, token records, storage locations, access state, and audit metadata.
- **CLI:** Spectre.Console-based command-line tool with both fully parameterized commands and interactive terminal UI workflows.

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
- A metadata database backend for file records, share records, token records, access counters, storage references, and audit metadata.

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

## Non-Goals

ShadowDrop should not become:

- A cloud drive.
- A general document management system.
- A team collaboration suite.
- A sync client.
- A public anonymous file host.
- A replacement for full encrypted messaging.

## Open Questions

1. Should separate-key CLI decrypt mode be the default for more sensitive files?
2. Should download bearer tokens be scoped to a file, a share, or both?
3. Should download bearer tokens have max-access counts in addition to expiration timestamps?
4. Should download links expose the original file name by default?
5. How should access counting distinguish between a new download and resumed range requests for the same download attempt?
6. Should failed or interrupted downloads consume access count if they are never resumed to completion?
7. How should sender authentication work in the first version beyond validating admin bearer tokens?
8. Should files have retention policies independent of share expiration?
9. Should revoked shares delete the underlying file, or only block access through that share?
10. What exact CLI command shape should be used for separate upload/share commands and a possible combined upload-and-share command?
11. What metadata is safe and useful to keep for audit purposes?
12. What should the queue file format look like for CLI-managed multi-file downloads?

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
- [x] The concept supports a JSON CLI config file at `~/.config/shadowdrop/config.json` for defaults such as server URL and admin token.
- [x] The concept requires CLI distribution for Linux, macOS, and Windows on x64 and arm64.
- [x] The concept requires Docker image distribution for x64 and arm64.
- [x] The concept supports multiple files per share while keeping each file individually addressable.
- [x] The concept includes a future queue file format for CLI-managed multi-file downloads.
