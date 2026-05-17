# Architecture Decisions

## ADR-2026-05-17-01: File-scoped direct-HTTP crypto for issue #14

**Status:** Accepted

**Context:** The original direct-HTTP implementation path bound decryption to share-scoped values, but uploaded files are encrypted before any share exists. That made normal uploads incompatible with direct-HTTP downloads.

**Decision:** For the MVP, direct-HTTP cryptographic binding is file-scoped. The implementation removes `shareId` from the content-key derivation inputs and from authenticated chunk metadata, leaving file-level values as the source of truth for decryption.

**Consequences:**
- Direct-HTTP now works with normal uploads because encryption depends only on upload-time-known values.
- Multiple shares of the same uploaded file intentionally reuse the same file-scoped cryptographic context in the MVP.
- If stronger per-share isolation is needed later, the next step is a stored upload-time encryption context rather than returning to share-derived binding.

**Related artifacts:**
- `ai-plans/0014-basic-download-endpoint.md`
- `ai-plans/0014-basic-download-endpoint.decision-options.md`
- `PROJECT_CONCEPT.md`
