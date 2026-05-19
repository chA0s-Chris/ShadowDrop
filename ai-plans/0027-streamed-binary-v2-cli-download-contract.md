## Rationale

Replace the current CLI resumable-download JSON/Base64 response with a streamed binary contract so the CLI and API stop
paying wrapper overhead before that shape hardens further. Because ShadowDrop is still in early development with no
active external users, this slice may replace the current CLI contract outright instead of preserving a compatibility
layer that only adds drag. This binary contract becomes the authoritative CLI download shape on release (not a "v2"
option), negotiated explicitly via `?mode=cli`.

## Acceptance Criteria

- [x] The CLI resumable-download path uses a single streamed binary response contract instead of the current JSON/Base64
  payload contract.
- [x] The API exposes an explicit, deterministic negotiation mechanism for selecting the CLI binary contract, and
  rejects ambiguous or unsupported contract selections with a generic client error.
- [x] The plan defines the full negotiation matrix for omitted/unknown `mode`, `mode=cli` requests on direct-HTTP
  shares, requests that also send direct-HTTP key material, requests that mix `Range` headers with legacy query
  parameters, and other mixed/ambiguous inputs.
- [x] Successful CLI responses return raw encrypted chunk bytes in the body and all metadata required for local
  decryption and resume (`firstChunkIndex`, `lastChunkIndex`, requested plaintext range, total plaintext size, chunk
  size, final chunk plaintext length) in a deterministic transport shape.
- [x] The CLI binary contract defines both request-side range inputs (standard `Range: bytes=...` header) and
  response-side HTTP semantics (200 OK, custom headers, no 206/Content-Range).
- [x] The CLI contract keeps the same authentication, optional bearer-token, expiration, and range-validation rules as
  the existing download path.
- [x] The API streams the encrypted chunk span directly to the response without JSON wrapping, Base64 encoding, or
  full-response buffering.
- [x] The CLI download flow can request the CLI contract (via `?mode=cli`), send a `Range: bytes=...` header for
  partial downloads, read the metadata, stream the encrypted bytes, decrypt locally, and resume partial downloads.
- [x] The CLI contract fails closed when metadata headers are missing, duplicated, malformed, semantically inconsistent,
  or paired with an unexpected media type or body length.
- [x] The legacy JSON/Base64 CLI contract producer/parser code is removed completely so the codebase has only one
  authoritative CLI download contract (the streamed binary shape via `?mode=cli`).
- [x] Automated tests cover API negotiation, metadata shape, streaming behavior, CLI consumption, resume behavior,
  Range header acceptance/rejection, and invalid/unauthorized request handling for the CLI binary contract.

## Technical Details

**Parameter Sprawl Mitigation:**

The CLI download contract currently carries several inputs across different boundaries: Range header, legacy
`plaintextStart`/`plaintextEndExclusive` query parameters, `?mode` selector, direct-HTTP key material, and
authentication tokens. To keep the contract explicit and prevent implementation guessing:

- **Single Request Model Consolidation:** Implement a single `CliDownloadRequest` value object (or struct) that
  encapsulates all validated inputs at the endpoint boundary:
  `{ mode, requestedPlaintextRange, shareId, bearerTokenIfPresent, directHttpKeyMaterialIfPresent }`. This eliminates
  ad-hoc parameter threading and makes contradictions visible at construction time rather than scattered across helpers.
- **No Implicit Parameter Inference:** All downstream service calls receive only the consolidated model; no individual
  parameters are passed separately. This keeps the contract boundary explicit.
- **Legacy Parameter Sunset:** The endpoint parser must detect legacy `plaintextStart`/`plaintextEndExclusive`
  parameters and reject them on all requests. Do not thread legacy parameter names into the service layer.

Keep direct-HTTP downloads as they are; this plan only changes the CLI-specific encrypted-subset contract.

**Transport Contract (Decided Shape):**

- **Response Body:** Raw encrypted chunk bytes, streamed directly without JSON wrapping or Base64 encoding
- **Response Headers:** Deterministic HTTP headers carry all metadata needed for local decryption and resume
  - `X-ShadowDrop-First-Chunk-Index`: integer (0-based chunk index where the encrypted span starts)
  - `X-ShadowDrop-Last-Chunk-Index`: integer (inclusive; 0-based chunk index where the encrypted span ends)
  - `X-ShadowDrop-Plaintext-Range-Start`: integer (byte offset in the plaintext file where decrypted output starts)
  - `X-ShadowDrop-Plaintext-Range-End`: integer (byte offset in the plaintext file where decrypted output ends,
    exclusive)
  - `X-ShadowDrop-Total-Plaintext-Size`: integer (full plaintext file size in bytes)
  - `X-ShadowDrop-Chunk-Size`: integer (plaintext bytes per encrypted chunk, typically 1MB or configured value)
  - `X-ShadowDrop-Final-Chunk-Plaintext-Length`: integer (plaintext length of the final chunk, which may be smaller than
    chunk size)
- **Content-Type:** `application/vnd.shadowdrop.cli-download` (custom media type prevents accidental misinterpretation
  and can evolve if needed)
- **Existing Metadata Headers:** CLI mode responses include the non-payload metadata headers that the CLI already
  depends on
  to preserve existing download behavior: `X-ShadowDrop-File-Name` (filename) and `X-ShadowDrop-File-Content-Type` (MIME
  type).
  These headers are part of the CLI response contract and must be present and properly sanitized per existing safety
  expectations. Any mode-indicator header that callers already depend on is also included. These headers are not
  optional; they are binding parts of the stable CLI contract.

This design avoids body preambles/footers, keeps payloads as raw encrypted bytes, and fits the existing ASP.NET Core
streaming model.

**Request / Negotiation Rules:**

- `?mode=cli` is the only selector for the streamed CLI binary contract.
- Omitted `mode` parameter routes to the direct-HTTP plaintext-decryption path (or fails appropriately for
  direct-HTTP-only shares). The legacy CLI v1 JSON/Base64 contract is removed immediately as part of this slice.
- Unknown or invalid `mode` values fail with generic `400 Bad Request`.
- **Legacy Query Parameters are Removed:** The parameters `plaintextStart` and `plaintextEndExclusive` are fully retired
  and not accepted on any request (CLI or omitted mode). Any request containing either legacy query parameter is
  rejected with generic `400 Bad Request`.
- Requests that mix `mode=cli` with direct-HTTP-only inputs (direct-HTTP key material in headers or query parameters)
  are rejected rather than heuristically choosing a contract; respond with generic `400 Bad Request`.
- Subset selection for CLI binary mode uses only the standard `Range: bytes=start-end` request header, interpreted
  against plaintext byte offsets. This is the only supported subset selector when `?mode=cli` is present.
- `mode=cli` must not be offered on shares where direct-HTTP-only decryption is enforced (i.e., shares without
  encryption key wrapping or CLI-decrypt capability); implementation should detect this and fail requests for
  `?mode=cli` with generic `400 Bad Request`.

**CLI HTTP Semantics:**

- Response success code: CLI responses use `200 OK` (not `206 Partial Content`). The CLI interprets response metadata
  headers to understand which plaintext range is being streamed; HTTP 206/Content-Range semantics are not needed.
- `Accept-Ranges` and `Content-Range` are not sent in CLI responses; they would be confusing and are redundant with the
  ShadowDrop metadata headers that define the plaintext span.
- `Content-Disposition` is not the authoritative metadata channel for CLI mode; filename/content-type continue via the
  ShadowDrop headers above.
- Prefer sending `Content-Length` for CLI responses whenever the encrypted span length is known so the CLI can detect
  truncation deterministically. If any path cannot provide it, the implementation must define the fallback integrity
  check explicitly.
- Request-side `Range` header parsing: validate `bytes=start-end` format against plaintext offsets and reject malformed,
  missing end markers, or overlapped/contradictory ranges with a generic `400` client error. If a valid `Range` is
  present but unsatisfiable (e.g., start >= total plaintext size), respond with `416 Range Not Satisfiable` with an
  empty response body and no metadata headers. The 416 response must not reveal total file size, format hints, or any
  payload information; failure must be indistinguishable from other non-leaky error cases.

**Wire Integrity Rules:**

**Request-Side Negotiation & Validation Matrix:**

Implementation must enforce the following decision table to keep behavior deterministic:

| Scenario                                  | Input                                                     | Validation                    | Action                                         |
|-------------------------------------------|-----------------------------------------------------------|-------------------------------|------------------------------------------------|
| **CLI Binary Mode (Explicit)**            | `?mode=cli` + valid `Range: bytes=start-end`              | Parse & satisfy               | 200 OK + custom headers + binary body          |
| **CLI Binary Mode (No Range)**            | `?mode=cli`, no Range header                              | Return full file span         | 200 OK + custom headers + all encrypted chunks |
| **CLI Binary Mode (Malformed Range)**     | `?mode=cli` + malformed `Range: bytes=...`                | Syntax invalid                | 400 Bad Request                                |
| **CLI Binary Mode (Unsatisfiable Range)** | `?mode=cli` + `Range: bytes=...` but start >= total size  | Out of bounds                 | 416 Range Not Satisfiable                      |
| **CLI Binary Mode (Legacy Query Params)** | `?mode=cli` + `plaintextStart` or `plaintextEndExclusive` | Ambiguous                     | 400 Bad Request                                |
| **CLI Binary Mode (Direct-HTTP Key)**     | `?mode=cli` + direct-HTTP key material in header/query    | Incompatible                  | 400 Bad Request                                |
| **Omitted Mode (Default)**                | No `?mode`, standard Range header (if present)            | Parse Range as HTTP semantics | Route to direct-HTTP plaintext decryption path |
| **Omitted Mode (Legacy Query Params)**    | No `?mode`, `plaintextStart`/`plaintextEndExclusive`      | Legacy params not supported   | 400 Bad Request                                |
| **Direct-HTTP Shares on CLI Mode**        | `?mode=cli` on direct-HTTP-only share                     | Not applicable                | 400 Bad Request                                |
| **Unknown Mode**                          | `?mode=unknown`                                           | Invalid                       | 400 Bad Request                                |

**Wire Integrity Rules:**

- Request-side: The API validates the `Range: bytes=...` header (if present) before processing. Malformed, overlapped,
  or unsatisfiable ranges fail with generic `400` or `416` without leaking file size or format hints. A `416 Range Not
  Satisfiable` response must have an empty body and no metadata headers, making it indistinguishable from other safe
  error
  cases.
- Response-side: The CLI reader validates all required metadata headers before treating the body as resumable data.
- Missing, duplicated, non-numeric, overflowed, contradictory, or otherwise unparseable metadata headers fail closed.
- The CLI reader also fails closed when the media type is not `application/vnd.shadowdrop.cli-download`.
- Header values must be internally consistent with each other, the requested plaintext window (from Range header), and
  the streamed body length.
- Resume logic should treat mid-stream interruption as recoverable only after persisting enough validated metadata to
  restart from the last durable plaintext byte.

**API Implementation:**

**Where Routing & Validation Logic Lives (Deterministic Placement):**

All mode negotiation and request validation must be **centralized in `DownloadEndpoints`** before calling service
methods. This prevents behavior from being inferred across multiple handlers:

1. **Endpoint Entry Point (`DownloadEndpoints`):** Parse and validate `?mode` query parameter; parse and validate
   `Range: bytes=...` header (if present); detect presence of legacy query parameters; return generic `400` if inputs
   are ambiguous or contradictory per the negotiation matrix above.
2. **Mode Routing Decision:** If `?mode=cli` is present, enforce that it is not combined with legacy query parameters or
   direct-HTTP key material; call a dedicated CLI-mode service path. If `?mode` is omitted or unknown, route to the
   direct-HTTP plaintext-decryption path.
3. **CLI-Mode Service Path:** Receive only the validated mode identifier, the plaintext byte range (or null for full
   file), and existing auth context. Do not re-parse mode or Range; the endpoint has already filtered contradictions.
4. **Service Returns:** Return a unified resolution model that exposes the raw encrypted stream, metadata values, and
   response content type. The endpoint then streams the response body and sets headers deterministically.

This structure ensures:

- No logic duplication across helpers
- Contradictory inputs are caught once and rejected early
- Mode selection and range interpretation are testable at the endpoint layer
- Service implementations do not guess about contract details

Evolve `DownloadEndpoints` and `DownloadFileService` so CLI requests explicitly negotiate the binary contract via query
parameter `?mode=cli` and receive a streamed encrypted chunk span plus stable metadata headers. Reuse existing
plaintext-range-to-chunk-span logic and authentication/authorization gates; the change is at the transport boundary, not
in the cryptographic model.

For `mode=cli` requests, parse the standard `Range: bytes=start-end` request header to select the plaintext window.
Reject requests that mix `Range` headers with legacy `plaintextStart`/`plaintextEndExclusive` query parameters. Map the
plaintext range to the required encrypted chunk span, validate that the span is satisfiable against the file, and return
either a streamed response or a generic error (400 for malformed Range, 416 for unsatisfiable).

`DownloadFileService` should return a resolution model that exposes the raw encrypted stream plus metadata values;
eliminate the JSON-wrapped `CliResumableDownloadContract` payload. Keep the response streaming-first: no Base64
encoding,
no JSON templates, no whole-range buffering. As part of that work, lock the exact mode-selection branch table and
Range-header parsing rules in one place so endpoint behavior is deterministic and unit-testable instead of inferred
across multiple handlers.

**CLI/Shared Implementation:**
Replace the JSON DTO parser workflow with a contract reader that validates response headers before consuming the body
stream, reads encrypted bytes incrementally, and feeds them into the existing local decryption/resume pipeline. The
request side should construct the standard `Range: bytes=start-end` request header based on resume state (or use the
full file for fresh downloads), avoiding the legacy `plaintextStart`/`plaintextEndExclusive` query parameters.

Metadata-only shared contracts are acceptable if they keep the payload off the wire. Because ShadowDrop has no active
users yet, remove the old JSON/Base64 parser/producer surface entirely instead of carrying dual code paths. Keep the
negotiation mechanism (`?mode=cli`) explicit so future transport changes have a clear hook. Removal includes
the obsolete shared DTOs, serializers, parsers, API producers, and tests that currently encode the JSON/Base64 contract
so the repository cannot accidentally preserve or reference two CLI download shapes.

**Testing:**
Lock the contract from both edges. API tests verify: successful CLI responses set the custom content type; metadata
headers match requested range and chunk span; streamed bodies contain exactly the encrypted chunk bytes;
invalid/unauthorized/expired/malformed/unsatisfiable requests fail with the same non-leaky status behavior as today;
and the negotiation matrix is covered for omitted/unknown `mode`, direct-HTTP-share requests, direct-HTTP key-material
mixing, and mixed range syntaxes. Specific Range header test coverage: valid `bytes=start-end` formats produce
correct plaintext windows; overlapped or malformed ranges fail with generic `400`; unsatisfiable ranges produce `416`
without file-size leakage; requests mixing `Range` headers with legacy query parameters (`plaintextStart`,
`plaintextEndExclusive`) are rejected.

CLI tests verify: header parsing rejects incomplete, duplicated, malformed, or inconsistent metadata; body-length and
media-type mismatches are detected; the CLI can construct valid `Range` headers for full-file and resume downloads;
the CLI can download, resume, and decrypt via the binary stream path; interruptions can resume from the last durable
plaintext byte; and unsupported-contract or bad-response failures are handled cleanly. Favor sociable tests around the
existing download pipeline rather than mocking the protocol.
