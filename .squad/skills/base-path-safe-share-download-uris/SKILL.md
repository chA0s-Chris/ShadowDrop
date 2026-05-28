---
name: "Base-Path Safe Share Download URIs"
description: "Build public share manifest/file URLs without dropping deployed app path prefixes, and normalize manifest transport failures at the same boundary."
domain: "cli-downloads, url-construction, error-handling"
confidence: "high"
source: "earned during issue #17 review revision"
---

## Context

ShadowDrop CLI download flows call public share endpoints such as `/d/{shareToken}` and
`/d/{shareToken}/files/{fileId}`. If the configured server URL includes a deployed app prefix (for example
`https://host/base-path/`), constructing request URIs with leading-slash paths silently resets the URI to the origin
root and breaks downloads outside root deployments.

The same boundary is also where manifest fetch failures need to become command-level errors instead of leaking raw
transport exceptions into higher-level queue or command handlers.

## Patterns

- Treat configured server URLs as **directory bases** before appending public share paths. Normalize the base URI to a
  trailing slash, then append relative `d/...` paths without a leading slash.
- When parsing an absolute share URL, preserve every path segment before the trailing `/d/{token}` as the server base.
  Do not collapse back to `scheme://authority/`.
- Normalize manifest transport faults (`HttpRequestException`, stream `IOException`, non-user-cancelled timeouts) into
  the CLI's command exception type at the manifest client boundary.
- Keep semantic failures distinct: malformed manifest JSON stays a metadata/contract error; transport failures stay
  connection errors.

## Examples

- `https://shadowdrop.test/base-path/` + `d/share-token` → `https://shadowdrop.test/base-path/d/share-token`
- `https://shadowdrop.test/base-path/d/share-token` parsed as an absolute share URL → server base
  `https://shadowdrop.test/base-path/`, share token `share-token`
- `ShareManifestClient.GetAsync(...)` catches manifest stream/network faults and rethrows
  `DownloadCommandException("Server connection failed.")` so direct CLI execution returns exit code 1 and queue mode can
  continue after logging a failed entry

## Anti-Patterns

- Building share URLs with `new Uri(serverUrl, "/d/...")`
- Stripping an absolute share URL down to `scheme://authority` and losing `/base-path/`
- Letting raw `HttpRequestException` or manifest stream `IOException` escape the manifest client
- Mapping manifest transport failures to local filesystem error messages
