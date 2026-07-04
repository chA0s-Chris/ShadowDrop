# Write MVP README and operator quickstart

> Issue: [#80](https://github.com/chA0s-Chris/ShadowDrop/issues/80)

## Rationale

The product has a working API, CLI, Docker packaging, and E2E coverage, but `README.md` is still
effectively empty (logo plus a link to the deployment hardening doc). Before MVP, operators and
recipients need enough documentation to run the container, configure the CLI, upload files, share
download credentials safely, and understand the direct HTTP versus CLI-decrypt trade-off.

The README itself must stay brief — as a guideline, at most ~200 lines — so it carries only the
introduction, badges, quick-starts, and pointers; the detailed operator documentation lives in
`docs/`. If that still feels too long once written, the README can be shortened later.

## Acceptance Criteria

- [x] `README.md` explains what ShadowDrop is and the intended self-hosted secure file handoff
  use case.
- [x] `README.md` keeps the existing logo header unchanged and places badges directly below it
  (e.g. MIT license, current release version, Docker pulls).
- [x] `README.md` states the distribution channels: the API image on Docker Hub
  (`chaos/shadowdrop`) and the CLI via the project's GitHub releases.
- [x] The documentation explains the Docker Hub tagging scheme: `1.2.3` (exact version), `1.2`
  (highest patch of that minor), `1` (highest `1.x.x`), and `latest` (latest production
  version, never pre-releases).
- [x] `README.md` contains a quick-start for the API (run the container) and a quick-start for
  the CLI (configure, upload, download) with short copy-pasteable examples.
- [x] `README.md` stays brief — as a guideline, at most ~200 lines of Markdown (excluding the
  logo/badge header); everything beyond the quick-starts is linked from the README and lives
  in `docs/`.
- [x] The documentation (README plus `docs/`) covers the Docker/container deployment path,
  including port `19423`, `/app/data` persistence, reverse-proxy TLS expectations, and
  `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`.
- [x] The documentation covers CLI configuration sources: `--server-url`, `--upload-token`,
  `SHADOWDROP_SERVER_URL`, `SHADOWDROP_UPLOAD_TOKEN`, and `~/.config/shadowdrop/config.json`.
- [x] The documentation includes copy-pasteable examples for upload, direct HTTP
  upload/download, separate-key CLI download, queue generation/download, `--secrets-out`, and
  `--embed-secrets`.
- [x] The documentation explains when generated URLs use the CLI-provided server URL, including
  reverse-proxy/public-hostname guidance.
- [x] The documentation explains security trade-offs: separate-key mode, direct HTTP mode,
  `sd-key` query leakage, `curl-command` header-based key delivery, bearer tokens, and
  `--insecure` versus `--cacert`.
- [x] The documentation states current MVP limitations, including incomplete release publishing
  if still true at implementation time.
- [x] The documentation explains that the CLI upload token is the admin bearer token (uploads
  go through `/api/admin/uploads`), including the consequence that uploading requires access
  to the admin exposure boundary described in `docs/DEPLOYMENT_HARDENING.md`.
- [x] Commands and option names in the documentation are verified against the CLI's `--help`
  output and existing tests.
- [x] A follow-up issue is filed for aligning the published CLI executable/install flow with
  the documented `shadowdrop` command name.

## Technical Details

This is a documentation-only change; no runtime behavior, API contract, CLI output, or
cryptographic format changes are made.

**README.md** stays brief (about 200 lines) and operator-focused:

- Keep the logo header exactly as it is; add a badge row directly below it (MIT license, latest
  release version, Docker pulls, and similar). Use standard shields.io-style badges pointing at
  the GitHub repo and the Docker Hub image.
- A short "what is ShadowDrop" introduction: self-hosted secure file handoff.
- An API quick-start: pull and run the Docker Hub image (`chaos/shadowdrop`) with port
  `19423` published, a volume on `/app/data`, and `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` set; note
  that TLS is expected to be terminated by a reverse proxy. Docker Hub is the only image
  registry for now.
- A CLI quick-start: download the CLI from the project's GitHub releases, configure it
  (server URL and upload token), and show one short upload and one short download example.
  The quick-start should make explicit that the upload token is the bootstrap admin token —
  the CLI sends `--upload-token` as a bearer token to `/api/admin/uploads`; there is no
  separate upload-token provisioning flow in the MVP.
- A documentation section linking into `docs/` for everything deeper. The existing links to
  `docs/DEPLOYMENT_HARDENING.md` may be restructured or folded into that section.

**docs/** carries the detail the issue asks for. Add new operator-focused pages (suggested split,
implementer may adjust): a deployment guide (container deployment, port `19423`, `/app/data`
persistence, reverse-proxy TLS and public-hostname guidance, `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`,
the Docker Hub image source), a CLI guide (installation from releases,
configuration precedence of flags over `SHADOWDROP_SERVER_URL`/`SHADOWDROP_UPLOAD_TOKEN`
environment variables over `~/.config/shadowdrop/config.json`, and copy-pasteable examples for
plain upload, direct-HTTP upload/download, separate-key CLI download, queue generation/download,
`--secrets-out`, and `--embed-secrets`, including when generated URLs use the CLI-provided server
URL), and a security trade-offs page (separate-key mode versus direct-HTTP mode, `sd-key`
query-string leakage versus the header-based `curl-command`, bearer-token handling, `--insecure`
versus `--cacert`, and the fact that uploads authenticate with the admin bearer token against
the admin surface, so upload access implies admin-boundary access). The deployment guide should
document the Docker Hub tagging scheme (`1.2.3`, `1.2`, `1`, `latest`; `latest` never points to
pre-releases). The existing `docs/DEPLOYMENT_HARDENING.md` may be changed when practical —
for example, cross-linking it from the new pages or moving overlapping content — as long as its
current guidance (admin endpoint exposure, reverse-proxy controls, direct-HTTP URL sensitivity)
remains available and linked.

A current-MVP-limitations section belongs wherever it fits best (README if it stays within the
size budget, otherwise a docs page linked from the README). Release publishing is incomplete
today: `release.yml` builds the artifacts and the multi-platform image but does not yet tag,
push, or publish them, and no workflow references Docker Hub yet. Expect the limitations section
to state this; re-check at implementation time in case publishing has landed by then.

The documentation assumes the CLI binary is named `shadowdrop` (a small install script may
handle the rename later). The current publish pipeline emits `ShadowDrop.Cli` as the executable
name inside the release archives; documenting `shadowdrop` is the agreed convention regardless,
so do not rename examples to `ShadowDrop.Cli`. As part of this work, file a follow-up issue to
close that gap before the first release — either set `<AssemblyName>shadowdrop</AssemblyName>`
in `ShadowDrop.Cli.csproj` or add a small install script that renames the binary on install.

Every command, option name, environment variable, and default must be verified against the actual
CLI `--help` output (top-level and subcommands) and the existing E2E/integration tests before
being written down. Because the Docker Hub image and release artifacts are not published yet,
verify the quick-start commands against a locally built image and CLI (the build pipeline already
has smoke-test infrastructure for the image). Do not document aspirational commands that are not
yet implemented. Badge targets that do not resolve yet (e.g. Docker pulls before the first Docker
Hub publish) should still be wired to the final image/repo coordinates (`chaos/shadowdrop`) so
they light up once publishing happens.

No automated tests need to be written for this documentation-only issue.
