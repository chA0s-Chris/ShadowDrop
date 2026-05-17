# Eliot — Issue #12 Upload Persistence Decisions

## Context
Issue #12 adds the first durable upload persistence slice in `ShadowDrop.Api`.

## Decisions

1. **Opaque local blob layout**
   - Local blob files are stored under `Storage:LocalRoot` using server-generated blob keys derived from the file id, with a two-character directory fan-out (`{first-two-hex}/{fileId}.blob`).
   - Original file names are persisted as metadata only and never influence filesystem layout.

2. **Owner-only filesystem permissions**
   - The local storage root and any derived blob directories are created with owner-only directory permissions where supported.
   - Blob files and the LiteDB metadata file are enforced to owner read/write only (`0600` equivalent) where supported.

3. **LiteDB shared-mode upload repository**
   - `LiteDbUploadedFileMetadataRepository` uses `ConnectionType.Shared`, matching the in-process access pattern already established for admin token storage.
   - This keeps WebApplicationFactory-based tests and future in-process readers from tripping exclusive file locks.

4. **Deterministic cross-layer cleanup**
   - Upload persistence writes the blob first, then metadata, and deletes the blob again if metadata persistence fails or if the written length does not match declared encrypted length.
   - The local blob storage implementation also deletes partially written blob files on write failures.
