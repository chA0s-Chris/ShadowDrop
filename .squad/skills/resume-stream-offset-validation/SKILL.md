---
name: "resume-stream-offset-validation"
description: "Fail closed when resumable stream state and seekable destination length disagree."
domain: "download-validation"
confidence: "high"
source: "earned"
---

## Context
Apply this when a resumable download or upload session accepts both a caller-supplied durable byte count and a seekable destination stream. Treat those as two independent pieces of state that must agree before any network request or write begins.

## Patterns
- For seekable destinations, validate `stream.Length == durableByteCount` before seeking or resuming.
- Reject mismatches before issuing the HTTP request; otherwise stale resume state can create gaps, zero-filled regions, or wrong-offset overwrites.
- Keep the resume contract strict: the durable counter is the next byte to request, and the seekable stream length is the bytes already persisted.
- Add regressions for both mismatch directions: shorter-than-durable and longer-than-durable destinations.

## Examples
- `src/ShadowDrop.Cli/Downloads/CliDownloadSession.cs`: resumable CLI downloads should not seek to `DurablePlaintextLength` unless the destination length proves that plaintext prefix is actually durable.
- `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadSessionTests.cs`: coverage should assert no request is sent when the destination length and durable offset disagree.

## Anti-Patterns
- Trusting a persisted resume offset without cross-checking the destination stream.
- Seeking beyond current length and letting later writes zero-fill the gap.
- Treating overlapping or truncated destination content as harmless just because the stream is seekable.
