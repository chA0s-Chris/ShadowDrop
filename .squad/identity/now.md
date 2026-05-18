---
updated_at: 2026-05-18T11:19:54.273+02:00
focus_area: Issue #15 — Range Requests & Resumable Downloads
active_issues: [15]
---

# What We're Focused On

**Issue #15: Range Requests and Resumable Downloads**

Implementing HTTP 206 Partial Content support with deterministic CLI-resumable download contracts on top of existing chunked encryption.

## Active Slices

- **Slice 1 (Eliot + Parker):** Direct-HTTP range infrastructure, 206 handling, selective decryption
- **Slice 2 (Sophie + Eliot + Parker):** CLI resumable contract, encrypted-subset response shape
- **Slice 3 (Parker):** Security verification, resumability tests, cross-mode coverage

**Branch:** `squad/15-range-requests-and-resumable-downloads` from `main`

**Decision:** Documented in `.squad/decisions/inbox/nate-issue-15-range-requests.md`

## Key Architecture Points

- Non-leaky error handling: no file size, token details, or range hints in 4xx/5xx responses
- Streaming-oriented: no full-file materialization; selective chunk extraction only
- Selective decryption: only decrypt required plaintext chunks, trim to exact byte range
- CLI contract locked: deterministic JSON response with encrypted chunks + metadata for local decryption
- All range requests enforce same auth/expiration gates as full-file downloads
