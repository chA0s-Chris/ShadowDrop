# Root AGENTS.md

`ShadowDrop` is a secure file-sharing solution.

## Implementation rules

Plans typically have acceptance criteria with check boxes. Check each box when you are finished with the corresponding criterion.

## General Rules for the Code Base

TBD

### Code Style

For the project's code style, refer to `CODESTYLE.md`.

## Local Development Commands

Run these from the repository root. The scripts and the coverage recipe use the local tools from
`dotnet-tools.json`, so run `dotnet tool restore` once after cloning.

| Purpose                         | Command                                                              |
|---------------------------------|----------------------------------------------------------------------|
| Apply the code style            | `./cleanup_code.sh`                                                  |
| Find code issues                | `./inspect_code.sh`                                                  |
| Complete test suite             | `dotnet test ShadowDrop.slnx`                                        |
| Unit and integration tests only | `dotnet test ShadowDrop.slnx --filter "TestCategory!=E2E"`           |
| End-to-end tests only           | `dotnet test tests/ShadowDrop.E2E.Tests/ShadowDrop.E2E.Tests.csproj` |
| Release parity                  | `dotnet build -c Release ShadowDrop.slnx`                            |

### Code style and inspections

Run `./cleanup_code.sh` when you are finished with a change. It applies ReSharper's `Zorn` profile to the files Git reports as changed, staged, or untracked.

`./inspect_code.sh` runs ReSharper's inspections over the same files and reports semantic findings. Only C# files are inspected, because `inspectcode` has no rules for the other file types `cleanup_code.sh` formats. Pass `--all` to inspect the whole solution; any further arguments are forwarded to `dotnet jb inspectcode`, so `-e=WARNING` narrows the report to warnings and above. Both scripts print `No matching files to process.` and do nothing when no file of a relevant type is affected.

Findings are advisory: the script reports them and exits 0 either way. A non-zero exit means the inspection itself could not run.

**Formatting is `cleanup_code.sh`'s responsibility.** `inspect_code.sh` does not report formatting deviations, because `inspectcode` ships nearly all of its formatting rules disabled and enabling them would mean changing the shared `ShadowDrop.slnx.DotSettings` for everyone working in the solution. Do not reach for the inspection script expecting whitespace, indentation, or brace findings.

Do not use `dotnet format`: it never reads `ShadowDrop.slnx.DotSettings` and therefore reports findings that contradict the profile this repository actually enforces.

### Tests

The end-to-end command needs no category filter because every test in that project is `[Category("E2E")]`, and it needs no publish step: `ProductArtifacts.BuildConfiguration` selects Debug or Release through `#if DEBUG` and reuses the existing build outputs.

Two observations that look like problems but are not:

- The filtered solution-level run prints `No test matches the given testcase filter` for `ShadowDrop.E2E.Tests.dll` and still exits 0. That assembly contains nothing but end-to-end tests, so an empty selection is expected and must not be read as a failure.
- Debug compiles two more tests than Release. `ENABLE_THROTTLE_DOWNLOAD` is defined for Debug only in `src/ShadowDrop.Api/ShadowDrop.Api.csproj` and `tests/ShadowDrop.Api.Tests/ShadowDrop.Api.Tests.csproj`, and it gates `ThrottledStreamTests` behind `#if`. This is by design.

When the coverage number is actually wanted:

```bash
rm -rf artifacts/test-coverage
dotnet test -c Release ShadowDrop.slnx --filter "TestCategory!=E2E" \
  --collect:"XPlat Code Coverage" --settings coverlet.xml \
  --results-directory artifacts/test-coverage
dotnet coverage merge artifacts/test-coverage/*/coverage.cobertura.xml \
  -f cobertura -o artifacts/test-coverage/coverage.cobertura.merged.xml
```

This uses the same collector, the same `coverlet.xml`, and the same `dotnet-coverage` tool as CI. The reset is required rather than tidy: `dotnet test` adds one result directory per run and the merge globs all of them, so leaving earlier runs in place folds their fragments into the number. `-c Release` matches what CI measures and additionally exercises `TreatWarningsAsErrors`, which `Directory.Build.props` sets for Release only.

The installer scripts have their own suites, which no `dotnet test` command covers:

- `bats tests/install.bats` covers the shell installer and needs `bats` on `PATH`.
- `Invoke-Pester -Path tests/install.Tests.ps1` covers the PowerShell installer and needs the `Pester` module; CI runs it on Windows under both `powershell` and `pwsh`.

`./build.sh Test TestEndToEnd` is the command CI runs, not a local recommendation. It bootstraps `build/Nuke.csproj`, builds a second Release output tree, and invokes `dotnet test` once per project sequentially, so it takes substantially longer and produces far more console output than the direct commands above.

## Production Code Rules

Read ./src/AGENTS.md for details about the production code.

## Testing Rules

Read ./tests/AGENTS.md for details about how to write tests.

## Plan Rules

Read ./ai-plans/AGENTS.md for details on how to write plans.

## Here is Your Space

If you encounter something worth noting while you are working on this code base, write it down here in this section. Once you are finished, I will discuss it with you, and we can decide where to put your notes.

- On Linux kernel 6.19+, the current `mongo:8.3` image exits during startup and cites `SERVER-121912` because the image bakes in `GLIBC_TUNABLES=glibc.pthread.rseq=0`. The Compose smoke target injects `glibc.pthread.rseq=1` through its temporary ignored override, while the operator-facing Compose file stays aligned with MongoDB's supported configuration.
