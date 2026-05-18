---
name: "header-control-char-sanitization"
description: "Sanitize persisted metadata before writing HTTP headers by removing all control characters, not just CR/LF."
domain: "api-security"
confidence: "high"
source: "earned"
---

## Context
Apply this when persisted or user-supplied metadata is echoed into HTTP response headers. Filtering only CR/LF is too narrow for custom headers because other control characters can still survive storage and break header validation or produce malformed responses. Fast-path scans must also account for Unicode C1 controls (`\u0080-\u009F`), not just ASCII controls.

## Patterns
- Validate the primary semantic header separately (for example, parse `Content-Type` and fall back safely if invalid).
- Independently sanitize any mirrored/custom header values before assignment.
- Remove **all** control characters with `Char.IsControl` (or an equivalent allow-list), then enforce any length cap.
- If you use a cheap pre-scan to skip allocation, the pre-scan must match the sanitizer's rule set. Scanning only `\u0000-\u001F` plus `\u007F` while sanitizing with `Char.IsControl` is inconsistent and leaks C1 controls through the fast path.
- Add an end-to-end regression that persists escaped control characters and verifies the response stays successful and header values contain no control bytes.

## Examples
- `src/ShadowDrop.Api/Downloads/DownloadEndpoints.cs`: `GetResponseContentType()` protects `Response.ContentType`, but `X-ShadowDrop-File-Content-Type` also needs full control-character stripping.
- `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs`: best place for a regression that uploads metadata containing escaped control characters and asserts the download response remains safe.

## Anti-Patterns
- Stripping only `\r` and `\n` while leaving NUL, DEL, C1, or other controls intact.
- Using a narrow ASCII-only detection gate before a broader sanitizer, so dangerous characters bypass sanitization entirely.
- Assuming validation of one framework-managed header automatically makes a separate custom header safe.
- Covering only unit-level sanitization without an HTTP-level regression.
