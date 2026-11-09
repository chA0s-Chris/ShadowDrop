# Adopt the repo-baseline pre-release

> Issue: [#248](https://github.com/chA0s-Chris/ShadowDrop/issues/248)

## Rationale

ShadowDrop keeps local copies of the code style, ReSharper settings, scripts, build properties and agent instructions that the maintainer's .NET repositories share, and these copies are older than the shared state in many places. [repo-baseline](https://github.com/chA0s-Chris/repo-baseline) is now the single source for that content, and ShadowDrop is the second pilot, as an application repository, before its `v1.0.0` release (chA0s-Chris/repo-baseline#4).

Adopting the pinned pre-release replaces the local copies with synced content, reduces the local files to what is specific to ShadowDrop, and runs `check` in CI so that shared content cannot drift again. The issue names `0.1.0`; this plan pins `0.2.0`, which carries the fixes from the first pilot (chA0s-Chris/repo-baseline#17, #18).

## Acceptance Criteria

- [x] `.repo-baseline.json` pins `0.2.0` with the profiles `common`, `dotnet` and `github`, and `check` against `v0.2.0` reports no findings.
- [ ] A `repo-baseline` job in `.github/workflows/ci.yml` runs `check` with the snippet from the repo-baseline README, and it passes on the adoption PR.
- [x] The root `Directory.Build.props` imports `.repo-baseline/Common.props` and keeps only `TargetFramework` and `EmbedUntrackedSources`; `tests/Directory.Build.props` imports `.repo-baseline/Tests.props` and keeps only `NoWarn` and `RootNamespace`.
- [x] Neither `Microsoft.SourceLink.GitHub` (BLD-6) nor `Microsoft.TestPlatform.TestHost` (TST-1) has a `PackageReference` or `PackageVersion`.
- [x] `ShadowDrop.slnx.DotSettings` contains only the injection of `.repo-baseline/ReSharper.DotSettings`; `.editorconfig`, `.gitignore`, `AGENTS.md` and `.github/copilot-instructions.md` contain only local content beside their managed blocks.
- [x] `AGENTS.md` no longer has the "Here is Your Space" section or its `mongo:8.3` note (AGT-1), and its remaining local text no longer describes the replaced script behavior (C#-only selection, `-e=WARNING`) or says that `Directory.Build.props` sets `TreatWarningsAsErrors` for Release only.
- [x] `scripts/test-inspect-code.sh` and its CI step are removed.
- [x] Every integration test class carries `[Category("Integration")]` (TST-3), `AGENTS.md` lists the fast-test command with `TestCategory!=Integration&TestCategory!=E2E`, and `tests/AGENTS.md` states the category rule. `dotnet test --filter TestCategory=Integration` selects exactly the three MongoDB persistence fixtures and `RustFsS3IntegrationTests`.
- [x] `renovate.json` extends `github>chA0s-Chris/repo-baseline//renovate/default` and otherwise contains only the test-image custom manager and the `mongo-5` and `rustfs` holds.
- [x] Each drift item listed in #248 is resolved, or recorded in this plan with the reason it stays.
- [x] The reformatting caused by the shared rules is a commit of its own and passes the Release build and the full test suite on its own. `.git-blame-ignore-revs` lists that commit.
- [x] `./inspect_code.sh --all` reports no findings at warning severity; each former finding is fixed or suppressed with a reason (CS-11).
- [x] The Release build has no warnings, `CI=true` builds of `ShadowDrop.slnx` and `build/Nuke.csproj` pass, and the full test suite passes, including the end-to-end tests.
- [x] Problems found in repo-baseline's profile content or tool are reported as issues in chA0s-Chris/repo-baseline after the maintainer approved their text.

## Technical Details

### Running the tool

Run the tool from the repo-baseline clone outside this repository, with that clone as the working directory. It reads `v0.2.0` from the clone's Git objects. `<path>` is the ShadowDrop working copy.

```bash
dotnet run tool/sync.cs -- init <path> 0.2.0 --name ShadowDrop --type application --forge github --repository-url https://github.com/chA0s-Chris/ShadowDrop --target-frameworks net10.0
dotnet run tool/sync.cs -- sync <path>
dotnet run tool/sync.cs -- check <path>
```

`init` creates `.repo-baseline.json`, `CLAUDE.md` and the three `.repo-baseline/` files, and skips every other existing file; `sync` then writes the synced files and blocks. `dotnet-tools.json` keeps its versions, which Renovate maintains.

### Drift listed in #248

Re-verified on `main` at `baf351a`; the Renovate merges up to `2882c2e` do not touch these files except `dotnet-tools.json`.

| Item | Resolution |
| --- | --- |
| Using order (CS-9) | `sync` replaces `CODESTYLE.md`; the shared `.editorconfig` block sorts usings without groups. |
| `WRAP_LIMIT` 160 (CS-5) | The local key is removed; the block sets `max_line_length = 180`. |
| Expression-bodied constructors and local functions `false:warning` (CS-6) | The local `.editorconfig` keys are removed; the block sets `false:none`. |
| `resharper_object_creation_when_type_not_evident = target_typed` (CS-7) | The local key is removed; the block sets `explicit`. |
| `RemoveRedundantParentheses` (CS-8) and `ArrangeArgumentsStyle` (CS-1) | The local `Zorn` profile is removed; the shared one applies. |
| `cleanup_code.sh`, `inspect_code.sh` (TOOL-1) | Replaced by `sync`. |
| Notes section in `AGENTS.md` (AGT-1) | Replaced by the managed block. The `mongo:8.3`/`GLIBC_TUNABLES` note is dropped: `docs/DEPLOYMENT.md` and the comment in `build/BuildPipeline.Compose.cs` already record it. |
| Tool manifest location (BLD-5) | Already the root `dotnet-tools.json`. |

### Further drift

- **Local files.** Every key of `ShadowDrop.slnx.DotSettings` is either shared or expressed by the `.editorconfig` block (`WRAP_LIMIT`, the primary-constructor severity, `PreferQualifiedReference`, `QualifiedUsingAtNestedScope`) or empty Rider state (`CustomToolsData`). The file keeps only the layer injection, in the form of the repo-baseline template with a new layer GUID. `.editorconfig` keeps a local `[*.cs]` section with `file_header_template`. `.gitignore` keeps `.idea`, `/src/ShadowDrop.Api/data/`, `.env` and `!.env.example`; the local `artifacts/` is dropped because the block's anchored `/artifacts/` covers the root and every nested `artifacts` directory lies under an ignored `bin/`. `.github/copilot-instructions.md` keeps only the FluentAssertions nullability section.
- **`AGENTS.md`.** The managed blocks replace "Implementation rules", the "Code Style" subsection, "Code style and inspections", "Plan Rules" and "Here is Your Space". The local part keeps the project description, the general rules, the commands table and the "Tests" subsection, and the pointers to `src/AGENTS.md` and `tests/AGENTS.md`. The tool-restore sentence moves to the block's wording, and the coverage paragraph names `.repo-baseline/Common.props`, which makes warnings errors in Release and in CI.
- **Build properties.** `Common.props` makes nullable warnings errors in every configuration (CS-4) and all warnings errors when `CI=true` (BLD-1). This also applies to `build/Nuke.csproj`, which inherits the root `Directory.Build.props` and is built in Debug by `build.sh` in CI. At `baf351a` the Nuke build failed under `CI=true` on NU1902/NU1903 advisories for `System.Security.Cryptography.Xml` and `.Pkcs` 10.0.12; Renovate PR #244 has since bumped them. `src/Directory.Build.props` keeps `GenerateDocumentationFile`, `PackageLicenseExpression` and `DefineConstants`. The plain `<NoWarn>` in `build/Nuke.csproj` stays; it overrides only `1591`, which is irrelevant there.
- **Test packages.** `Tests.props` provides every package that `tests/Directory.Build.props` references today except `Microsoft.TestPlatform.TestHost`; duplicates would fail the Release build with NU1504. `Microsoft.NET.Test.Sdk` brings TestHost transitively.
- **Inspection script tests.** `scripts/test-inspect-code.sh` tests the replaced local script; repo-baseline's `tests/scripts` covers the shared one.

### Integration categories (TST-3)

The integration tests are the Testcontainers-based classes in `tests/ShadowDrop.Api.Tests/Infrastructure`: the abstract `MongoPersistenceIntegrationTests`, whose three subclasses inherit its categories, and `RustFsS3IntegrationTests`. Both get `[Category("Integration")]`; their existing `MongoIntegration` and `S3Integration` categories stay as narrower filters. The CI filter `TestCategory!=E2E` is unchanged, so CI still runs the integration tests. Moving integration tests into an `.Integration` namespace (TST-4) is out of scope.

### Renovate

Keep `$schema`, the preset reference, the custom manager for `MongoDbTestImages.cs`/`RustFsTestImages.cs`, and the `mongo-5` and `rustfs` holds. Everything else is in the preset (chA0s-Chris/repo-baseline#5). The only intended difference is the group name "Test SDK/MTP" becoming "Test SDK", which gives future grouped pull requests a new branch name.

### Commits and merge

The branch keeps these commits apart, in this order:

1. Adoption: tool output, reduced local files, props imports, package, script-test and Renovate changes.
2. Reformatting: only the output of `./cleanup_code.sh --all` under the new settings. At `baf351a` it changed 190 files, mostly explicit object creation (CS-7), plus member order (CS-13) and usings (CS-9).
3. `.git-blame-ignore-revs` (new) with the full hash of commit 2.
4. Integration categories.
5. Inspection fixes.
6. The CI job.

If the reformatting commit fails the Release build or the test suite, fix the cause in a commit before it and regenerate the reformatting commit. The PR must be merged with a merge commit; a squash or rebase merge changes the hash in `.git-blame-ignore-revs`. If the branch is rebased before merging, update the hash.

### Inspection findings

With the shared settings applied, `inspect_code.sh --all` reported 129 findings at `baf351a`, 25 of them in production code. The largest groups are `AccessToDisposedClosure` in the test projects (65) and inconsistent synchronization in `ShadowDrop.Api` (14). Fix a finding when a small, behavior-neutral change removes it; otherwise suppress it with `// ReSharper disable once <InspectionId>` followed by a comment line with the reason. A production finding whose fix would change behavior is brought to the maintainer instead of being fixed here. `inspection.suppressedIds` stays empty.

### CI

Add a job that clones the pinned release into `${RUNNER_TEMP}` and runs `check` as in the repo-baseline README section "In CI". Give it `contents: read`, use `actions/checkout` with `persist-credentials: false` and `actions/setup-dotnet` for .NET 10, pinned by tag like the other jobs in the workflow. ShadowDrop has no aggregating result job, so the job's own status is the check.

### repo-baseline findings

None found. `init`, `sync` and `check` of `v0.2.0` worked as documented, and the fixes from the first pilot (chA0s-Chris/repo-baseline#17, #18) held: `check` reported the two missing props imports until they were added.
