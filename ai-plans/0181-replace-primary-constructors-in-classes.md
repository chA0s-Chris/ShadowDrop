# Replace primary constructors in classes

> Issue: [#181](https://github.com/chA0s-Chris/ShadowDrop/issues/181)

## Rationale

Class primary constructors hide the dependency-to-field mapping, silently capture parameters into compiler-generated state, and make constructor accessibility, validation, and disposal ownership harder to read. This plan removes every class primary constructor from `src`, `tests`, and `build`, replaces them with explicit constructors that assign explicitly declared members, and records the prohibition in `CODESTYLE.md` plus `.editorconfig` so the pattern does not creep back in via IDE suggestions.

Positional records (including positional `record struct`) stay allowed and untouched — the concern is classes only.

## Acceptance Criteria

- [x] No class declaration in `src`, `tests`, or `build` uses a primary constructor.
- [x] Positional records and positional record structs are unchanged.
- [x] Every replacement constructor preserves the original accessibility, parameter order, default parameter values, base-constructor invocation, argument validation, and runtime behavior.
- [x] Constructor dependencies are stored in `_camelCase` private readonly fields (or initialized properties where the existing type already exposes them as properties), following `CODESTYLE.md` naming rules.
- [x] XML documentation that referenced primary constructor parameters via `<paramref>` still resolves — the parameters are documented with `<param>` tags on the new constructor or the prose is reworded.
- [x] `CODESTYLE.md` gains an explicit rule prohibiting primary constructors on classes while allowing positional records.
- [x] The unclosed ```` ```csharp ```` fence in the Documentation section of `CODESTYLE.md` is closed.
- [x] `.editorconfig` sets `csharp_style_prefer_primary_constructors = false:suggestion` so IDE0290 no longer suggests the conversion back.
- [x] `ShadowDrop.slnx.DotSettings` disables ReSharper's *Convert into primary constructor* suggestion, verified by the suggestion no longer being reported.
- [x] `bash build.sh Test` passes: all production projects build warning-free in Release via the test projects' references (`TreatWarningsAsErrors` is on for Release; the build path suppresses CS1591, CS1998, and CS4014) and the fast unit/integration suite is green.
- [x] `dotnet build ShadowDrop.slnx -c Debug -warnaserror` succeeds, so the code behind `ENABLE_THROTTLE_DOWNLOAD` is compiled and held to the same warning bar as Release.
- [x] The end-to-end suite (`bash build.sh TestEndToEnd`) passes locally or in CI.
- [x] No unrelated formatting, renaming, or cleanup is included in the change — member reordering required by the repository's enforced file layout does not count as unrelated.

## Technical Details

### Scope

Roughly 72 classes under `src` and 67 under `tests` currently use primary constructors. `build` contains none, and `build/Nuke.csproj` is excluded from the solution build (`<Build Project="false" />` in `ShadowDrop.slnx`), so no compiler check covers it — its inclusion in the acceptance criteria is a verification-only scan and no file changes are expected under `build`.

The work is unevenly distributed, and not where the type names below might suggest: `ShadowDrop.Cli` holds 43 of the 72 production occurrences and `ShadowDrop.Api` the other 29, while `ShadowDrop.Shared` and `ShadowDrop.HealthProbe` have none and need no changes at all. On the test side: `ShadowDrop.Api.Tests` 43, `ShadowDrop.Cli.Tests` 23, `ShadowDrop.HealthProbe.Tests` 1, and `ShadowDrop.Shared.Tests` / `ShadowDrop.E2E.Tests` none.

This ships as a single atomic PR rather than a stacked chain: the change is mechanical and behavior-preserving, and splitting it would leave `main` temporarily inconsistent with the rule it just adopted.

The affected types cluster into a few shapes, each with its own pitfall:

- **DI-injected services and repositories** (`MongoUploadCredentialRepository`, `UploadCredentialService`,
  `AdminTokenService`, `ShareListService`, `ShareRevocationService`, `DownloadFileService`, …) — the plain case:
  declare `private readonly` fields and assign them in the constructor. Preserve any existing
  `ArgumentNullException.ThrowIfNull` guards and add none that were not there before.
- **Exception types declared as expression bodies**, e.g.
  `public sealed class UploadCredentialValidationException(String message) : Exception(message);` — these must become a normal body with an explicit constructor forwarding to `base(...)`. Note `ShareListValidationException`, which passes a *fixed* message to the base and keeps its own parameter as state; the replacement must retain both.
- **Nested private/internal helper classes** inside `DownloadEndpoints` (`DownloadStreamResult`, `NoStoreResult`,
  `StatusDownloadResult`) and `DownloadFileService` (`LengthLimitingReadStream`). `StatusDownloadResult` has a default parameter value (`String? body = null`) that must survive.
- **Stream decorators** (`ThrottledStream`, `CountingWriteStream`, `S3SeekableReadStream`, `LengthLimitingReadStream`)
  and `IDisposable` wrappers (`S3ReadResponse`). Disposal ownership is deliberate and documented in these types — do not start or stop forwarding `Dispose`/`DisposeAsync` while moving the parameters into fields.
- **Field initializers that reference primary constructor parameters**, such as `ThrottledStream`'s
  `_bytesPerSecond = bytesPerSecond > 0 ? … : throw …`. Move that validation into the constructor body (or keep it as a conditional expression assigned in the constructor); the throwing behavior and exception type, parameter name, and message must stay identical. Note that a field initializer runs *before* the base constructor call while a constructor body runs *after* it; for the `Stream` and `Exception` bases involved here the difference is immaterial, but check the base constructor before making the same move elsewhere.
- **CLI types** — the single largest group: command handlers (`DownloadCommandHandler`, `UploadCommandHandler`,
  `UploadRawCommandHandler`, `ShareCreateCommandHandler`, `InteractiveUploadCommandHandler`, …), progress reporters and their factories (`SpectreDownloadProgressReporter`, `PlainTextDownloadProgressReporter`,
  `DownloadProgressReporterFactory`), HTTP clients (`ShareManifestClient`, `UploadCommandExecutor`), and configuration resolvers (`CliConfigurationResolver`). Shape-wise these are the same plain field-assignment case as the Api services above, but they carry more than half the production work and 19 of the 43 declarations open a multi-line parameter list — several dependencies each, unlike the single-dependency example under *Mechanics*. They are called out because no other type name in this taxonomy points at them.
- **The one abstract class with a primary constructor** — `UploadedFileRepositoryDecorator` in
  `tests/ShadowDrop.Api.Tests/Uploads/UploadSweepServiceTests.cs`, with `FailFirstAccountingRepository` and
  `FailFirstMetadataDeleteRepository` deriving from it through the base list. Convert the base and both derived types together. The compiler emits an abstract class's primary constructor as `protected`, not `public` (verified against the net10 compiler; a sealed class's is `public`), so write `protected UploadedFileRepositoryDecorator(IUploadedFileMetadataRepository inner)`. Writing `public` here silently widens accessibility and nothing in the build or test suite would catch it.
- **Test fixtures and test helper classes** under `tests` — same mechanical treatment.

### Mechanics

Work file by file, not with a blanket regex rewrite. The safe shape per class is:

```csharp
internal sealed class Example : IExample
{
    private readonly IDependency _dependency;

    public Example(IDependency dependency)
    {
        // Guard shown for illustration only: keep it if the primary constructor already validated,
        // drop it otherwise. Adding new guards changes observable behavior.
        ArgumentNullException.ThrowIfNull(dependency);
        _dependency = dependency;
    }
}
```

Only add `ThrowIfNull` where the primary constructor version already validated; this refactoring must not change observable behavior, including which exceptions are thrown.

Place the new fields and constructors according to the file layout pattern in `ShadowDrop.slnx.DotSettings` (fields before constructors, constructors in their own ordered entry). Run `./cleanup_code.sh` over the changed files — the member reordering it performs is expected and is not the "unrelated formatting" the acceptance criteria rule out.

Watch for parameters that were captured implicitly in more than one member — with a primary constructor the compiler creates one backing field, so a single `private readonly` field is the faithful replacement. Conversely, a parameter used only inside the constructor (for example, only to compute another value) must *not* become a field.

XML documentation is the main non-mechanical part: `<paramref name="inner"/>` inside a type-level `<remarks>` no longer binds once the parameter moves to a constructor. Four production files combine a primary-constructor class with `<paramref>` and need checking: `Downloads/ThrottledStream.cs`, `Shares/ShareCleanupService.cs`, `Cli/Downloads/Progress/SpectreDownloadProgressReporter.cs`, and `Cli/Tls/CliHttpClientFactory.cs`. Either reword the prose to name the member (`<c>_inner</c>` / "the inner stream") or move the parameter documentation onto the constructor. In Release, warnings are errors, so unresolved `paramref` targets (CS1734) will fail the build in projects that produce XML docs.

`ThrottledStream`, `DownloadThrottling`, and the throttling middleware hook are behind `#if ENABLE_THROTTLE_DOWNLOAD`
(defined for `ShadowDrop.Api` in Debug only — `src/ShadowDrop.Api/ShadowDrop.Api.csproj`). A Release build never compiles them, and no Nuke target builds Debug by default: `TargetBuildConfiguration` is a Nuke `[Parameter]` that defaults to `Release`. Overriding it (`--target-build-configuration Debug`) still would not enforce the warning bar — `TreatWarningsAsErrors` applies to Release only, and the Nuke build path additionally passes `/nowarn:CS1591;CS1998;CS4014`. So the one configuration in which this code compiles is the one where a broken `<paramref>` stays a harmless warning. Verify it with an explicit `dotnet build ShadowDrop.slnx -c Debug -warnaserror`.

### Documentation and analyzer configuration

- `CODESTYLE.md`: add the rule under a suitable section (a short "Types and Constructors" subsection, or an entry in *General*/ *Formatting*). It must state that classes always use explicit constructors, that primary constructors on classes are not permitted, and that positional records and record structs remain fine. Keep it short and in the existing bullet style.
- `CODESTYLE.md`: the ```` ```csharp ```` fence opened at the method documentation example is never closed, which makes the rest of the document render as code. Close it right after the example method signature.
- `.editorconfig`: add `csharp_style_prefer_primary_constructors = false:suggestion` next to the other
  `csharp_style_prefer_*` entries (they live around lines 75–94), matching their severity convention. IDE0290 reports nothing once the option is `false`, so the severity suffix is inert — it is there only for consistency with its neighbors.
- `ShadowDrop.slnx.DotSettings`: suppress ReSharper's *Convert into primary constructor* suggestion. The `.editorconfig` entry only covers Roslyn/IDE0290, but this repository also uses ReSharper (`cleanup_code.sh`, and `dotnet jb inspectcode` per `CODESTYLE.md`), which would otherwise keep suggesting the pattern this plan bans. The file currently holds no `InspectionSeverities` entries, so the key path has to be added — likely
  `/Default/CodeInspection/Highlighting/InspectionSeverities/=ConvertToPrimaryConstructor/@EntryIndexedValue` set to `DO_NOT_SHOW`. Confirm the exact inspection ID from the tooling (`dotnet tool restore`, then `dotnet jb inspectcode`, or the IDE's *Inspection options*) instead of trusting the identifier written here, and confirm the suggestion is actually gone afterwards: a wrong ID yields an entry that silently does nothing. The `Zorn` cleanup profile contains no primary-constructor task, so `cleanup_code.sh` does not rewrite constructors — this is about suppressing suggestions, not preventing an automated regression.

### Verification

- `./cleanup_code.sh` — applies the repository's ReSharper cleanup profile to the changed files, which is what places the new fields and constructors in the enforced layout order. Run it before the builds; it needs `dotnet tool restore`
  for the `jb` tool.
- `bash build.sh Test` — the repository's canonical entry point. Note that `Test` depends on `BuildTests`, not on
  `Build`: it compiles the test projects and, transitively, every production project they reference, in Release with warnings as errors (minus the suppressed CS1591, CS1998, and CS4014). It runs the fast unit/integration suite, excluding `TestCategory=E2E` and globbing the E2E project out. Use `bash build.sh Build Test` if an explicit solution-wide build is wanted.
- `bash build.sh TestEndToEnd` — the end-to-end smoke tests, which the `Test` target deliberately skips. They require
  `curl` on PATH; run them locally where the environment allows, otherwise rely on CI, which runs both targets.
- `dotnet build ShadowDrop.slnx -c Debug -warnaserror` — the only way to compile and warning-check the code behind
  `ENABLE_THROTTLE_DOWNLOAD`. Debug does not inherit `TreatWarningsAsErrors`, hence the explicit flag.
- A final repository-wide scan confirming no primary constructors remain:

  ```bash
  grep -rEn '^[[:space:]]*((public|internal|private|protected|file|sealed|abstract|static|partial)[[:space:]]+)*class[[:space:]]+[A-Za-z_]\w*(<[^>]*>)?[[:space:]]*\(' \
    --include='*.cs' src tests build --exclude-dir=obj --exclude-dir=bin
  ```

  It must return nothing. At the time of writing it matched 139 declarations — 72 under `src` and 67 under `tests` — which is the baseline for the counts above. Constraining the modifier run to a fixed keyword set is what keeps records out: neither `record class` nor `record struct` can match, because `record` is not in the set. Do not "simplify" this by filtering the output through `grep -v record` instead — that also discards five real primary-constructor classes whose parameter happens to be named `record`.
- A check that `record`/`record struct` declarations are byte-identical to `main`.

No new tests are required: this is a behavior-preserving refactoring already covered by the existing suite.
