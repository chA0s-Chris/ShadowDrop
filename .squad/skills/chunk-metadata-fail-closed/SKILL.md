---
name: "chunk-metadata-fail-closed"
description: "Validate chunk-derived lengths with checked arithmetic so corrupt metadata cannot produce bad download offsets."
domain: "download-validation"
confidence: "high"
source: "earned"
---

## Context
Apply this when persisted upload metadata is reused to compute encrypted offsets, plaintext spans, or resumable download contracts. Stored chunk counts, chunk sizes, and plaintext lengths are not trustworthy enough for unchecked arithmetic.

## Patterns
- Use `checked` arithmetic for chunk-count * chunk-size math and for any narrowing cast back to `Int32`.
- Validate derived final chunk length stays within `1..ChunkSize` whenever metadata describes a chunked file.
- Throw or surface a fail-closed error before opening or streaming content when metadata is inconsistent.
- Add hostile-metadata regressions that cover underflow, overflow, and `> ChunkSize` remainder cases.
- In direct-streaming download setup, translate `InvalidDataException` and early `IOException` failures into the existing generic invalid-request result so corrupt metadata and hostile streams fail closed without leaking internals.

## Examples
- `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`: `GetFinalChunkPlaintextLength()` now validates corrupt chunk metadata before building CLI resumable contracts or encrypted offsets.
- `tests/ShadowDrop.Api.Tests/Downloads/DownloadFileServiceTests.cs`: regression cases cover negative remainder, oversized remainder, arithmetic overflow, and direct-HTTP fail-closed setup paths.
- `src/ShadowDrop.Api/Downloads/DownloadFileService.cs`: `TryOpenDirectHttpContentAsync()` treats direct-HTTP setup-time metadata/I/O failures as `InvalidRequest`, matching the CLI-decrypt branch.

## Anti-Patterns
- Using unchecked `Int64` multiplication/subtraction for persisted metadata math.
- Narrowing `Int64` remainders to `Int32` without `checked`.
- Assuming `ChunkCount` and `PlaintextLength` are internally consistent because they came from storage.
- Continuing to stream when derived chunk lengths are zero, negative, or larger than the configured chunk size.
- Surfacing raw metadata-validation or setup I/O exceptions from the direct-HTTP branch after the blob stream is already open.
