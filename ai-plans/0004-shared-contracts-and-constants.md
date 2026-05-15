## Rationale

Define the shared contracts needed by both the API and CLI before implementing the first upload and download slices. These contracts should capture stable protocol names, queue-file shape, shared wire/file metadata, and encryption format descriptors without becoming a dumping ground.

## Acceptance Criteria

- [x] Shared constants exist for `ShadowDrop-Key` and `sd-key`.
- [x] Shared constants exist for the CLI config path components `.config`, `shadowdrop`, and `config.json`.
- [x] Shared constants exist for known version strings, including queue format version `1.0` and the initial encryption
  format version.
- [x] Queue file models represent the agreed JSON format.
- [x] Queue file models support `shadowDrop`, `queueVersion`, `target`, `shareId`, and file entries.
- [x] Queue file entries support `fileId`, `fileName`, `length`, and optional `plaintextSha256`.
- [x] Shared wire/file metadata contracts exist for share id, file id, encryption format version, algorithm id, chunk
  size, chunk count, KDF salt, and optional plaintext SHA-256.
- [x] Shared API DTOs exist only where both API and CLI need the same wire contract.
- [ ] Server-only persistence entities such as token records and audit records are not added to `ShadowDrop.Shared`.
- [ ] Shared contracts avoid storing plaintext secrets such as admin tokens, download bearer tokens, decryption keys, or `sd-key` values.
- [x] Queue file validation rejects missing required fields, empty file lists, invalid URLs, invalid lengths, and
  malformed SHA-256 values.
- [x] Automated tests cover JSON serialization and deserialization of the queue format, including omitted optional
  `plaintextSha256`.
- [x] Automated tests cover queue file validation failures.
- [x] Automated tests verify that JSON property names match the queue file format exactly.

## Technical Details

Implement these contracts in `ShadowDrop.Shared`. Keep the project intentionally small. Only add types that are genuinely shared by API and CLI or define stable wire/file formats. Do not add server-only persistence entities such as token records, audit records, LiteDB documents, or internal API repository models.

The queue file format should be JSON and should match the concept:

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

Represent ids as strings to allow opaque ids later. Treat `plaintextSha256` as optional metadata used for post-download verification. Do not include bearer tokens or decryption keys in queue files.

Queue JSON property names should match the queue file format exactly: `shadowDrop`, `queueVersion`, `target`, `shareId`, `files`, `fileId`, `fileName`, `length`, and `plaintextSha256`. If custom naming, converters, required-property behavior, or unknown-field handling is needed, define it explicitly and cover it with tests so the queue file remains stable across API and CLI changes.

Prefer an explicit queue file parser and validator over relying only on deserialization. Validation should reject missing required fields, empty file lists, invalid target URLs, negative or otherwise invalid file lengths, and malformed SHA-256 values. Validation errors should be suitable for CLI display and should not include secrets.

Shared encryption metadata should describe file format, not persistence. Include the initial encryption format version, AES-256-GCM algorithm identifier, share id, file id, chunk size, chunk count, non-secret per-share KDF salt, and optional plaintext SHA-256. The actual share encryption secret, derived file keys, bearer tokens, and direct-download key material must not be represented in persisted or serialized shared metadata contracts.
