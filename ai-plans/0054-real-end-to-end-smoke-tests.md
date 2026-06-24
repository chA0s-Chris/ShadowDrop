## Rationale

ShadowDrop has strong unit and in-process integration coverage, but it should also have a narrow real end-to-end smoke
test that proves the shipped API and CLI artifacts work together as users run them. The test should build the product,
start the API as a separate process, invoke the CLI as a separate process, and verify the two primary download modes
against a live local HTTP endpoint. This plan addresses
[#54](https://github.com/chA0s-Chris/ShadowDrop/issues/54).

## Acceptance Criteria

- [ ] A real end-to-end test target builds or publishes the API and CLI before running the smoke scenarios.
- [ ] The E2E harness starts `ShadowDrop.Api` as a separate process on an isolated localhost port with temporary metadata
  and storage paths, waits for `/health`, and reliably terminates the API process afterward.
- [ ] The queue-download scenario uploads multiple files with the CLI using explicit `--server-url`, `--upload-token`,
  and `--queue-out`, captures the emitted share key, downloads the generated queue with `--queue`, `--output-root`, and
  `--share-key`, and byte-compares every downloaded file with the original.
- [ ] The direct-HTTP scenario uploads one file with the CLI using environment variables for server URL and upload token
  plus `--direct-http`, captures the emitted `download-url:` value from the direct HTTP output added by #55, downloads it
  with `curl`, and byte-compares the result with the original.
- [ ] The direct-HTTP E2E scenario is implemented only after #55 is merged, or remains pending until #55's
  `download-url:` output exists.
- [ ] The smoke tests are isolated, non-parallel, deterministic, and clean up temporary files, directories, and child
  processes even when a scenario fails.
- [ ] The E2E smoke tests are marked with an NUnit `[Category("E2E")]` so the default fast unit/integration `Test` target
  excludes them by filter (`TestCategory!=E2E`), and a dedicated E2E target runs only `TestCategory=E2E`, wired as a
  separate developer/CI entry point with CI running it as a separate job or step.
- [ ] Test-project or local E2E documentation explains how to run the real E2E smoke tests locally and lists required
  external tools, including `curl`.

## Technical Details

Add a dedicated E2E test project or equivalent build target rather than folding these process-level tests into the
existing API or CLI test suites. The harness should publish or otherwise build the API and CLI artifacts into a temporary
or artifacts directory, then execute those artifacts through `ProcessStartInfo` so the test covers the real entrypoints,
stdout/stderr behavior, exit codes, configuration binding, HTTP binding, and filesystem side effects.

Use a dynamically allocated localhost port and pass API configuration through environment variables such as
`ASPNETCORE_URLS`, `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN`, `ShadowDrop__Metadata__LiteDbPath`,
`ShadowDrop__Storage__LocalRoot`, `ShadowDrop__ApiExposure__EnableAdminOperations`, and
`ShadowDrop__ApiExposure__EnablePublicDownloads`. The harness should poll `GET /health` until the API is ready or a
short timeout expires, and it should capture API stdout/stderr so failures are diagnosable.

Because the CLI upload commands authenticate against the admin-guarded `/api/admin/uploads` endpoints, the CLI's upload
token is the admin bearer token. The harness must therefore generate a single secret and use the same value for both the
API's `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` and the CLI's upload token (`--upload-token` / `SHADOWDROP_UPLOAD_TOKEN`);
otherwise uploads are rejected with 401.

For the queue scenario, create three small input files with distinct contents. Invoke the CLI upload command with
explicit `--server-url`, `--upload-token`, and `--queue-out` arguments. Parse the normal CLI output for the `share-key:`
line and then invoke the CLI download command with the generated queue path, an explicit `--output-root`, and the parsed
share key. Assert successful exit codes and byte-for-byte equality for all downloaded files.

For the direct-HTTP scenario, create one input file and invoke the CLI upload command with `--direct-http` while supplying
server URL and upload token through `SHADOWDROP_SERVER_URL` and `SHADOWDROP_UPLOAD_TOKEN`. This scenario depends on the
direct HTTP output fix in #55: parse the `download-url:` line, invoke `curl` against that file endpoint, and compare the
downloaded bytes with the original input file. Do not treat `share-url:` as curl-ready; it is the manifest URL. Implement
this scenario only after #55 is merged, or leave it explicitly pending until #55's output contract is available.

The implementation should avoid relying on fixed ports, global machine state, user config files, or long sleeps. It
should fail clearly when `curl` is unavailable: `curl` is a required prerequisite for the direct-HTTP scenario, not
optional, so its absence is a hard test failure everywhere (no skip path). Keep the suite intentionally small: it is a
product smoke test, not a replacement for the existing focused coverage.

Mark the E2E smoke tests with an NUnit `[Category("E2E")]` attribute and keep the `*.Tests.csproj` naming convention. The
default Nuke `Test` target (which globs `**/*.Tests.csproj`, see `build/BuildPipeline.Test.cs`) must then exclude this
category by default via a test filter (e.g. `TestCategory!=E2E`) so the fast unit/integration loop stays fast. Add a
dedicated Nuke E2E/smoke target that runs only `TestCategory=E2E`, invoked as a separate developer/CI entry point; CI can
then run it as a separate job or step.
