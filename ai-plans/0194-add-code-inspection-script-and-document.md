# Add code inspection script and document local development commands

> Issue: [#194](https://github.com/chA0s-Chris/ShadowDrop/issues/194)

## Rationale

`cleanup_code.sh` applies ReSharper's `Zorn` formatting profile, but nothing runs ReSharper's *inspections* from the shell. `dotnet format` is not a substitute: it never reads `ShadowDrop.slnx.DotSettings` and therefore reports findings that contradict the profile the repository actually enforces. A companion `inspect_code.sh` closes that gap.

The root `AGENTS.md` documents no way to run the test suite, so the choice falls to whoever happens to be working in the repository. The obvious candidate — `./build.sh` — is the wrong local default: it is roughly twice as slow as a direct `dotnet test`, produces an order of magnitude more console output, and reports a coverage figure computed from coverage fragments accumulated by every previous local run. Documenting the direct commands and fixing that last defect makes the local loop both faster and trustworthy.

## Acceptance Criteria

- [ ] `inspect_code.sh` exists at the repository root, is executable, and by default inspects only the files Git reports as changed or staged, using the same change detection as `cleanup_code.sh` narrowed to the file types `inspectcode` reports on.
- [ ] `inspect_code.sh --all` inspects the whole solution, and any remaining arguments are forwarded to `dotnet jb inspectcode`.
- [ ] With no matching changed files and without `--all`, the script reports that and exits without invoking `dotnet jb inspectcode`.
- [ ] The script prints a text report with one finding per line using absolute file paths, and states explicitly when no issues were found.
- [ ] The script exits 0 when the inspection completes, regardless of whether findings were reported, and exits non-zero when the inspection itself fails to run.
- [ ] The ReSharper caches and the report file are written into a gitignored location, so a run leaves the working tree clean.
- [ ] `ShadowDrop.slnx.DotSettings` is unchanged and no formatting inspections are enabled.
- [ ] The root `AGENTS.md` documents `cleanup_code.sh` for applying the code style and `inspect_code.sh` for finding code issues, and states that formatting deviations are `cleanup_code.sh`'s responsibility because `inspectcode` does not report them.
- [ ] The root `AGENTS.md` documents the complete test suite, the unit-and-integration-only run, and the end-to-end-only run as direct `dotnet test` commands, together with the coverage recipe including its results-directory reset and the two installer test suites, without stating timings or test counts.
- [ ] The root `AGENTS.md` records the harmless no-match message for the E2E assembly and the Debug/Release test-count difference, and mentions `./build.sh Test TestEndToEnd` only as the command CI runs.
- [ ] The Nuke `Test` target cleans its coverage directory before collecting, so the reported coverage reflects only the current run even when earlier fragments exist.
- [ ] Every command documented in `AGENTS.md` has been executed and behaves as described.
- [ ] The existing test suite passes and `dotnet build -c Release ShadowDrop.slnx` reports no warnings.

## Technical Details

### `inspect_code.sh`

Mirror `cleanup_code.sh`: build the file set from `git diff --name-only --diff-filter=ACM` plus its `--cached` counterpart, and hand it to `dotnet jb inspectcode ShadowDrop.slnx` through `--include`. The `**/<git-relative-path>` mask form that `cleanup_code.sh` already produces is accepted by `inspectcode` unchanged; a bare `**/<directory>/**.cs` style mask is not, and reports `No files to inspect were found`.

Narrow the copied extension filter to `cs`. `cleanup_code.sh` also matches `csproj`, `json`, `sh`, `slnx`, and `config` because it formats them, but `inspectcode`'s rule set has no MSBuild, JSON, or project-file inspections — grouping `--dumpIssuesTypes` by language yields only C#, C++, VB, XAML, ASPX/HTML, Razor, RESX, and route-template categories, and of those this repository contains only C#. A change touching only non-C# files would otherwise spend a full analysis to report nothing.

The empty-set guard is load-bearing rather than cosmetic: `inspectcode` analyzes every file in the solution when `--include` is absent, so a script that simply passes an empty mask list silently escalates "nothing changed" into a full-solution scan. `cleanup_code.sh` already models the right behaviour with its `No matching changed files to process.` branch.

Design points:

- `--all` inspects the whole solution. Remaining arguments pass through to `inspectcode`, keeping `-e=WARNING`, `--project=<name>`, and similar usable without editing the script.
- Point `--caches-home` into the gitignored `tmp/` directory. A cold run takes about 70 seconds; with warm caches it settles around 25 seconds, and that difference is entirely the cache.
- Emit `--format=Text` together with `--absolute-paths` so each finding is one greppable, clickable line (`/abs/path.cs:<line> <description>`). Without that flag the report uses Windows separators even on Linux (`src\ShadowDrop.Cli\CliApplication.cs:26`), which no editor or `grep` pipeline can follow. Write the report under `tmp/` before echoing it: text sent straight to stdout interleaves with the tool's own log.
- Run at `inspectcode`'s default `SUGGESTION` severity, which is the level the baseline below was measured at, and leave `-e=WARNING` to the caller when the noise gets in the way.
- Keep the script advisory with respect to findings: report them and exit 0. The current code base yields roughly 40 findings in `ShadowDrop.Cli` alone at that severity (`Use object initializer`, `Property 'Major' can be made private`, `Type cast is redundant`), so a non-zero exit would fail on the first run and teach the reader to ignore it. This does not extend to the tool's own failures — unrestored local tools, a solution that does not build, an invalid argument — which must still fail loudly.
- Do not read stderr noise as failure. One cold-cache run emitted a `JetBrains.Util.LoggerException` with a full stack trace from solution-wide analysis, still exited 0, and still produced a valid report, so a stack trace on its own says nothing about whether the inspection completed. The tool's exit status is the signal that matters. `--verbosity=WARN` keeps the ordinary log quiet without suppressing that class of noise.
- As with `cleanup_code.sh`, the script assumes the local tools from `dotnet-tools.json` are restored; it does not run `dotnet tool restore` itself.

### Formatting stays with `cleanup_code.sh`

`inspectcode` does not report formatting deviations, and this is a property of the tool rather than of the configuration. A deliberately mangled file (`public static Int32 Add(   Int32 a,Int32 b )`, `if(a>b){ return a; }`) produced only semantic findings. `dotnet jb inspectcode --dumpIssuesTypes` lists 79 rules in the `FormattingIssues` category and nearly all ship as `{"enabled": false, "level": "none"}`; only `BadChildStatementIndent` and `BadControlBracesIndent` are enabled by default. Adding an `IncorrectFormatting` severity entry to a settings file changes nothing — the rule ids are the individual `Bad*` inspections.

Enabling them would mean adding on the order of 79 `InspectionSeverities` entries to `ShadowDrop.slnx.DotSettings`. That file is a shared settings layer, so the change would also alter the Rider and ReSharper experience for everyone working in the solution. It is deliberately out of scope: `cleanup_code.sh` stays the single authority for formatting, `inspect_code.sh` is the semantic checker, and `AGENTS.md` must say so plainly enough that nobody reaches for `inspect_code.sh` expecting whitespace findings.

### Local development commands in `AGENTS.md`

A new section in the root `AGENTS.md` documents the commands. It carries each command and its purpose only: no timings and no test counts, because both drift — timings with the machine, counts with every test added. The measured figures below come from a 32-core Linux machine with warm build outputs and exist in this plan as justification for the choice of commands, not as numbers to copy into `AGENTS.md`. The one exception is the Debug/Release test-count difference further down, which is documented because it explains a surprising observation rather than promising a figure.

| Purpose            | Command                                                              | Measured             |
|--------------------|----------------------------------------------------------------------|----------------------|
| Complete suite     | `dotnet test ShadowDrop.slnx`                                        | 1 m 13 s, 1283 tests |
| Unit + integration | `dotnet test ShadowDrop.slnx --filter "TestCategory!=E2E"`           | 1 m 09 s, 1276 tests |
| End-to-end only    | `dotnet test tests/ShadowDrop.E2E.Tests/ShadowDrop.E2E.Tests.csproj` | 14 s, 7 tests        |
| Apply code style   | `./cleanup_code.sh`                                                  | —                    |
| Find code issues   | `./inspect_code.sh`                                                  | —                    |
| Release parity     | `dotnet build -c Release ShadowDrop.slnx`                            | seconds              |

The end-to-end command needs no filter because every test in that project is `[Category("E2E")]`, and it needs no Nuke publish step: `ProductArtifacts.BuildConfiguration` selects Debug or Release through `#if DEBUG` and reuses the existing outputs.

Coverage, for when the number is actually wanted:

```bash
rm -rf artifacts/test-coverage
dotnet test -c Release ShadowDrop.slnx --filter "TestCategory!=E2E" \
  --collect:"XPlat Code Coverage" --settings coverlet.xml \
  --results-directory artifacts/test-coverage
dotnet coverage merge artifacts/test-coverage/*/coverage.cobertura.xml \
  -f cobertura -o artifacts/test-coverage/coverage.cobertura.merged.xml
```

This uses the same collector, the same `coverlet.xml`, and the same `dotnet-coverage` local tool as CI, and takes about 1 m 15 s — instrumentation costs roughly 4 seconds over a plain run. Verified end to end: five fragments merged, 90.96 % line rate. `-c Release` matches what CI measures and additionally exercises `TreatWarningsAsErrors`, which `Directory.Build.props` sets for Release only; the Debug equivalent reports 89.52 %.

Two behaviours need documenting so nobody spends time chasing them:

- The solution-level filtered run prints `No test matches the given testcase filter 'TestCategory!=E2E' in .../ShadowDrop.E2E.Tests.dll` and still exits 0. This is precisely what the Nuke `Test` target sidesteps by excluding that project from its glob; at solution level it is harmless and must not be read as a failure.
- Debug compiles two more tests than Release. `ENABLE_THROTTLE_DOWNLOAD` is defined for Debug only in `src/ShadowDrop.Api/ShadowDrop.Api.csproj` and `tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj`, gating `ThrottledStreamTests` behind `#if`. Debug runs 1283 tests, Release 1281. This is by design and nothing needs fixing.

`./build.sh Test TestEndToEnd` is documented only as the command CI runs. It is not a local recommendation: it bootstraps `build/Nuke.csproj` (~18 s), builds a second Release output tree, and invokes `dotnet test` once per project sequentially through `CombineWith`, so `ShadowDrop.Api.Tests` (1 m 06 s) cannot overlap `ShadowDrop.Cli.Tests` (23 s). The result is 2 m 24 s and 477 lines of ANSI-coloured `DBG` output against 1 m 09 s and 35 lines for the direct run.

The installer tests are a third suite that neither Nuke test target covers: `bats tests/install.bats` and `tests/install.Tests.ps1`, each with its own CI job.

### Stale coverage fragments in the Nuke `Test` target

`Test` in `build/BuildPipeline.Test.cs` merges `{CoverageDirectory}/**/coverage.cobertura.xml` but never cleans `CoverageDirectory`; only the separate `Clean` target does, through `ArtifactsDirectory.CreateOrCleanDirectory()`. Locally the directory therefore accumulates one result directory per run forever. On the machine used for these measurements `artifacts/test-coverage/` held 312 directories and a single `./build.sh Test` merged 311 fragments into the figure it reported, which is why its "90.77 %" and the 90.96 % measured from a clean directory disagree.

Clean `CoverageDirectory` at the start of the target, before `DotNetTest` runs. CI is unaffected either way because it always starts from a fresh checkout, so this is a local-correctness fix. `ReportTestCountAndCoverage` also globs `ArtifactsDirectory` for `*.Tests.xml`, but those files are overwritten by name on each run rather than accumulated, so they need no equivalent treatment.

### Tests

No new automated tests are required, and this is a deliberate decision rather than an omission. `inspect_code.sh` is a developer convenience wrapper with no product behaviour, exactly like the untested `cleanup_code.sh`; the repository's shell-script tests (`scripts/test-calculate-docker-tags.sh`, `tests/install.bats`) cover scripts whose output feeds release automation or reaches end users. The `AGENTS.md` change is documentation. The Nuke change is verified by running `./build.sh Test` against a directory that already contains fragments and confirming the reported coverage matches a clean-directory run.
