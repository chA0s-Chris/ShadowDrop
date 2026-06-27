# CLI: support self-signed/custom TLS trust for all network commands

> Issue: [#75](https://github.com/chA0s-Chris/ShadowDrop/issues/75)

## Rationale

The ShadowDrop CLI works fine over `http` and over `https` when the server presents a certificate with a valid, trusted chain. However, many users terminate TLS at a reverse proxy using self-signed certificates, which the CLI currently cannot talk to: every network command shares a single `HttpClient` built with a default handler in `CliApplicationServices.CreateDefault()`, with no way to relax or customize certificate validation. These users are effectively locked out of using HTTPS.

The goal is to support self-signed / privately-issued certificates without forcing users back to plain HTTP, while keeping the safe path (verified trust) the default and clearly flagging the unsafe path.

Two complementary options are introduced:

- `--cacert <file>` — the preferred, safe path. Trust a specific CA / self-signed certificate as an additional trust anchor; the presented chain is still validated against it, so MITM remains defeated while self-signed setups work.
- `--insecure` (curl-style, alias `-k`) — convenient fallback that fully disables certificate validation. Because this re-enables MITM exposure (notably for the upload token and download bearer token carried in headers), it must emit a loud warning to stderr whenever active.

Both options apply to all network commands and are configurable via flag and `SHADOWDROP_*` environment variable only (no config-file entry), with the flag taking precedence.

## Acceptance Criteria

- [ ] `--cacert <file>` is supported on all network commands and makes the CLI trust the provided certificate as an additional trust anchor while still validating the chain.
- [ ] `--insecure` (with `-k` alias) is supported on all network commands and disables certificate validation entirely.
- [ ] A clear warning is written to stderr whenever `--insecure` is active.
- [ ] Both settings are configurable via flag and a `SHADOWDROP_*` environment variable, with the flag taking precedence. No config-file support.
- [ ] `--cacert` and `--insecure` used together produce a sensible, documented behavior (e.g. `--insecure` wins with a warning, or the combination is rejected).
- [ ] CLI help text for both options clearly communicates the security trade-offs.
- [ ] The options apply to `upload`, `upload raw`, `download`, `queue create`, `share create`, and the interactive upload/download flows.
- [ ] Documentation updated to describe the self-signed / reverse-proxy use case and both options.
- [ ] Automated tests need to be written.

## Technical Details

The current `HttpClient` is constructed in `CliApplicationServices.CreateDefault()` *before* command-line arguments are parsed, so a per-invocation TLS setting requires deferring `HttpClient` / handler construction until after parsing (or rebuilding the handler post-parse). The resolved TLS options must therefore be threaded through to wherever the shared `HttpClient` is created and wired into the service setup consumed by the command handlers.

The TLS behavior lives on the message handler:

- `--insecure`: set `HttpClientHandler.ServerCertificateCustomValidationCallback` (or `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback`) to accept all certificates.
- `--cacert`: load the provided certificate and validate the presented chain against it as a custom trust anchor — build an `X509Chain` with the extra root via a custom validation callback, rather than blindly accepting.

Suggested design notes (leaving implementation freedom to the implementer):

- Add the two options as shared options registered across the network commands in `CliApplication.CreateCommandModel()`, mirroring how `--server-url` is shared today, and add them to the `CliCommandModel` record.
- Resolution of the effective values should follow the established flag → env var precedence pattern used in `CliConfigurationResolver` (env vars such as `SHADOWDROP_CACERT` and `SHADOWDROP_INSECURE`). Deliberately do not extend `CliConfigFile`.
- Introduce a small TLS-options abstraction that produces the configured `HttpMessageHandler` / `HttpClient`, so the construction currently in `CliApplicationServices.CreateDefault()` can be parameterized after parsing.
- The stderr warning for `--insecure` should be emitted once per invocation through the existing `StandardError` writer available to the handlers.

### Related

- #34 (Support app-managed HTTPS) — server-side TLS binding; complementary but distinct from this client-side trust configuration.
