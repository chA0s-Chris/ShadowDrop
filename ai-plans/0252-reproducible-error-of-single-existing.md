# Reproducible error of single (existing) file during queue download

> Issue: [#252](https://github.com/chA0s-Chris/ShadowDrop/issues/252)

## Rationale

Downloading a shared file whose stored name contains a non-ASCII character such as `ñ` fails with HTTP 500. `DownloadEndpoints.DownloadStreamResult` writes the stored name (`DisplayName ?? OriginalFileName`) and the stored content type unencoded into the `X-ShadowDrop-File-Name` and `X-ShadowDrop-File-Content-Type` response headers. Kestrel rejects non-ASCII response header characters with `InvalidOperationException` before any body is sent. The name comes from server-side metadata, so editing the queue file's `fileName` or `outputPath` cannot avoid the failure.

The goal is that every stored file name and content type can be downloaded in both download modes, while the existing header-injection protection stays in place.

## Acceptance Criteria

- [x] A shared file whose stored name contains non-ASCII characters downloads successfully in both direct-HTTP mode and `?mode=cli`.
- [x] `X-ShadowDrop-File-Name` and `X-ShadowDrop-File-Content-Type` always contain only visible ASCII, and decoding them as percent-encoded UTF-8 yields exactly the sanitized, length-limited value.
- [x] The direct-HTTP `Content-Disposition` header still carries the full Unicode file name through `filename*`.
- [x] Control characters, including C1 controls, are still removed from the file name and content type before they reach any response header, and the `download` fallback still applies. The sanitized value is limited to 255 UTF-8 bytes without splitting a character, so all filename headers together stay below 4 KB.
- [x] Automated tests cover a non-ASCII file name and a non-ASCII stored content type in both download modes, and the existing download header tests are updated for the encoded format.
- [x] An E2E smoke test uploads a file with a non-ASCII name and downloads it successfully from the real API server, both through the CLI and through direct HTTP with curl.

## Technical Details

- Both mirror headers always use percent-encoded UTF-8 (`Uri.EscapeDataString` semantics), applied after control-character sanitization and the length limit. Pure-ASCII values that contain reserved characters change too (for example `my file.bin` → `my%20file.bin` and `application/octet-stream` → `application%2Foctet-stream`); that is intended, so clients can always decode unambiguously.
- The 255-UTF-8-byte limit replaces the current 500-UTF-16-character cut in `SanitizeHeaderValue`, which can split a surrogate pair. It applies before encoding and feeds both mirror headers and `Content-Disposition`. Each encoded header therefore stays at or below 765 characters, within nginx's default `proxy_buffer_size`. `docs/DEPLOYMENT.md` documents running behind nginx. Tests cover a long name made of multi-byte characters, including a surrogate pair at the limit.
- In direct-HTTP mode the stored content type is also written to the real `Content-Type` header. `GetResponseContentType` therefore falls back to `application/octet-stream` unless the value contains only characters Kestrel accepts in a header value (visible ASCII, space, tab).
- `Content-Disposition` keeps receiving the sanitized, unencoded name; `ContentDispositionHeaderValue.FileNameStar` already performs its own RFC 5987 encoding.
- Contract change: `ai-plans/0027-streamed-binary-v2-cli-download-contract.md` describes these headers as part of the stable CLI contract. No client in this repository currently reads them, because the CLI takes the file name from the share manifest. Record the new encoding where the header constants are defined (`DownloadHeaderConstants`) so external consumers can decode it. Historical plans stay unchanged.
- `TestApiFactory` hosts the API on `TestServer`, which does not apply Kestrel's response-header character validation. The unit tests therefore cannot reproduce the 500. Instead, they assert that the headers are ASCII-only and decode back to the original value. The E2E suite starts the real API on Kestrel (`ApiServerProcess`), so the E2E smoke test is what covers Kestrel's actual header validation.
- Out of scope: CLI and queue-file handling, and upload-time file name validation. Non-ASCII names stay valid.
