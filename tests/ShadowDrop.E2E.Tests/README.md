# ShadowDrop.E2E.Tests

Real end-to-end smoke tests. Unlike the unit and in-process integration suites, these tests build the API
and CLI as separate artifacts and run them as **real OS processes**, then exercise the two primary download
modes against a live local HTTP endpoint. The goal is to prove that the shipped artifacts, process startup,
configuration binding, HTTP binding, upload, share creation, and download paths work together the way users
run them — not to duplicate the focused coverage that already exists elsewhere.

## What the suite does

On first use the suite builds `ShadowDrop.Api` and `ShadowDrop.Cli` into a temporary directory (no compile-time
project reference exists, so the only coupling is through built executables, stdout/stderr, exit codes, HTTP,
and the file system). Each scenario then:

- starts `ShadowDrop.Api` on a dynamically allocated loopback port with temporary metadata and storage paths,
  waits for `GET /health/ready`, captures the API's stdout/stderr, and reliably terminates it afterwards;
- runs the real CLI as a child process;
- byte-compares every downloaded file against the original input.

Three scenarios are covered:

1. **Queue download** — uploads three files with explicit `--server-url`, `--upload-token`, and `--queue-out`,
   parses the printed `share-key:`, downloads the generated queue with `--queue`, `--output-root`, and
   `--share-key`, and verifies every file.
2. **Single-file download** — uploads one file, then downloads the share with neither `--queue` nor `--out` and
   verifies the file landed at `./<original-filename>` in the CLI's working directory.
3. **Direct HTTP download** — uploads one file with `--direct-http`, configured through the
   `SHADOWDROP_SERVER_URL` and `SHADOWDROP_UPLOAD_TOKEN` environment variables, parses the printed
   `download-url:`, downloads it with `curl`, and verifies the bytes.

Each test is isolated, `[NonParallelizable]`, deterministic, and cleans up its temporary files, directories,
and child processes even when it fails.

## Required external tools

- **.NET SDK** — used to build and run the API and CLI artifacts (`dotnet` must be on `PATH`).
- **curl** — required by the direct-HTTP scenario. It is a hard prerequisite, not optional: if `curl` cannot
  be launched the test fails with a clear message rather than being skipped.

## Running locally

Every test is tagged `[Category("E2E")]` and is therefore **excluded from the default fast test loop**.

Run only the E2E smoke tests:

```bash
dotnet test tests/ShadowDrop.E2E.Tests --filter TestCategory=E2E
```

Or via the Nuke build (dedicated target):

```bash
./build.sh TestEndToEnd
```

The default `Test` target excludes this category (`--filter TestCategory!=E2E`), so the normal
unit/integration loop stays fast and does not build or start the product artifacts.
