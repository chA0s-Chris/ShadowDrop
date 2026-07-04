# Document direct-HTTP download URL sensitivity

> Issue: [#101](https://github.com/chA0s-Chris/ShadowDrop/issues/101) (part of umbrella [#97](https://github.com/chA0s-Chris/ShadowDrop/issues/97))

## Rationale

`DirectHttpDownloadUrlFactory.Create` puts the base64-encoded share key material — from which the
decryption key is derived — in the `sd-key` query parameter.
The server does not log this value and the emitted `curl-command` uses the safer
`ShadowDrop-Key` header form, but browser-friendly `download-url` values can still be captured by
browser history, referrer headers, reverse proxies, access logs, or other systems that record full
URLs. The release documentation should make this sensitivity explicit so operators and users do not
treat direct-HTTP URLs as ordinary links.

## Acceptance Criteria

- [ ] `DEPLOYMENT_HARDENING.md` contains a "Direct-HTTP download URL sensitivity" section stating
  that direct-HTTP `download-url` values are as sensitive as the file contents because the URL
  carries the share key material in the `sd-key` query parameter.
- [ ] `README.md` links to the new section in `DEPLOYMENT_HARDENING.md`.
- [ ] The documentation tells users not to send, paste, store, or proxy direct-HTTP URLs through
  channels that log full URLs, including browser history, referrers, chat systems, and intermediary
  HTTP logs.
- [ ] The documentation distinguishes the browser-friendly `download-url` from the emitted
  `curl-command`, which passes the key in the `ShadowDrop-Key` header and omits `sd-key` from the
  request URL.
- [ ] The change is documentation-only; no runtime behavior, API contract, CLI output, or
  cryptographic format changes are made.

## Technical Details

Add a concise "Direct-HTTP download URL sensitivity" section to `DEPLOYMENT_HARDENING.md` and link
it from `README.md`. This is the operator-facing documentation for the pre-release review tracked
by #97; if a dedicated release notes document is created later, it can reference this section.

The text should name the exact shape of the sensitive link:
`/d/<share-token>/files/<file-id>?sd-key=<base64-key-material>`. State that possession of this URL
is equivalent to possession of the plaintext file for the lifetime of the share, and that systems
which record complete URLs can retain the key material even after the user has stopped using the
link. Keep the guidance practical: use direct-HTTP `download-url` only in contexts where URL logging
is acceptable, prefer the header-based `curl-command` for command-line transfers, and revoke or
expire shares when a URL may have been exposed.

No automated test changes are expected for this documentation-only issue unless the implementation
adds or updates a generated documentation index that already has coverage.
