# Remove unnecessary null-forgiving operators

> Issue: [#182](https://github.com/chA0s-Chris/ShadowDrop/issues/182)

## Rationale

The null-forgiving operator (`!`) silences the compiler instead of proving that a value is non-null. Where it is used out of convenience, it hides invariants that nullable flow analysis could verify and makes later refactorings silently unsafe. The goal is to audit every null-forgiving expression in `src`, `tests`, and `build`, replace the unnecessary ones with compiler-verifiable nullable flow, keep the few that genuinely express invariants the compiler cannot infer, and record the resulting rules in `CODESTYLE.md` and `tests/AGENTS.md` so they are respected in new code. Runtime behavior and public contracts stay unchanged.

## Acceptance Criteria

- [x] Every null-forgiving expression in production code under `src` has been reviewed, using the `dotnet jb inspectcode` worklist described below as the enumeration.
- [x] Unnecessary production uses are replaced with nullable flow the compiler can verify (validation that returns the proven values, guard clauses, validated locals, pattern matching, accurately typed APIs, and nullable-flow attributes where they genuinely apply).
- [x] Null-forgiving expressions in `tests` and `build` are reviewed against the `dotnet jb inspectcode` worklist and, because that inspection misses the FluentAssertions-narrowed category, against the compiler itself; intentional invalid-null test inputs (e.g. `null!` passed to argument-validation tests) are preserved.
- [x] `!` that is redundant because FluentAssertions or NUnit.Analyzers already establish non-nullness (assertion subjects, fields assigned in `[SetUp]`/`[OneTimeSetUp]`) is removed.
- [x] Remaining null-forgiving expressions represent deliberate invariants that cannot reasonably be expressed through normal nullable flow, and each one in `src` either carries a short comment explaining the invariant or is listed with a one-line justification in the PR description.
- [x] Nullable reference type analysis remains enabled; no nullable warnings are suppressed globally, via `#pragma`, or via `.editorconfig` severity downgrades.
- [x] `CODESTYLE.md` documents the preferred nullable-flow style and the narrow cases in which the null-forgiving operator is acceptable.
- [x] `tests/AGENTS.md` states the test-specific rules so new tests do not reintroduce redundant `!`: no `!` after a FluentAssertions assertion, no `= null!` on members assigned in `[SetUp]`/`[OneTimeSetUp]`, `null!` only for deliberately invalid arguments. This deliberately extends the documentation scope of issue #182, which names only `CODESTYLE.md`; the PR description should say so.
- [x] Runtime behavior and public contracts remain unchanged; no unrelated formatting or cleanup is included.
- [x] The existing automated test suite passes unchanged and the solution builds without warnings in Release configuration (`TreatWarningsAsErrors` is on in Release); new tests are expected only if an internal API is restructured in a way the suite does not already cover.

## Technical Details

### Scope and inventory

The audit covers roughly 35 null-forgiving expressions in `src`, ~300 in `tests`, and a handful in `build`.

Start by enumerating them mechanically rather than by grepping for `!`: the repository already provides the JetBrains ReSharper command-line tools (`jetbrains.resharper.globaltools` in `dotnet-tools.json`, used by `cleanup_code.sh`). Run `dotnet tool restore`, then `dotnet jb inspectcode ShadowDrop.slnx --format=Xml --output=<report>`, and filter the report to two inspections:

- **`RedundantSuppressNullableWarningExpression`** ("Redundant nullable warning suppression expression") reports every `!` the compiler would accept without it. This is the audit worklist and the evidence behind the first acceptance criterion. It is a `WARNING` and enabled by default, so no `.editorconfig` or `ShadowDrop.slnx.DotSettings` change is needed.
- **`ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract`** catches the complementary case, where the `!` is genuinely required but the *guard* is the problem — it flags `QueueFileParser.cs:193` today, the composite boolean behind replacement 2 below.

Pass `--project=<name>` to narrow a run while iterating; the full-solution pass is considerably slower. Note that the C# compiler itself never warns about a redundant `!`, which is why a mechanical enumeration matters: removing one is silent whether it was needed or not.

**The inspection is necessary but not sufficient.** Running it over the whole solution reported only 15 redundant expressions, all in tests, and none of the FluentAssertions-narrowed ones — ReSharper does not model the `[NotNull]` post-condition on `Should()` the way Roslyn does, so it misses that entire category (and it also missed `QueueCreateCommandHandler.cs:63`, where the compiler was already narrowing). The authority for the test sweep is therefore the compiler: remove the candidate `!` in bulk, build in Release with `TreatWarningsAsErrors`, and restore only those the compiler demands back. That loop is sound and complete, and it converges — a second full cycle restored everything it stripped, confirming a fixed point.

The production occurrences cluster in a few places and should be addressed by category rather than one by one:

- `ShadowDrop.Cli/Downloads/DownloadCommandHandler.cs` and `ShadowDrop.Cli/Uploads/UploadCommandHandler.cs` hold the majority. They dereference members of deserialized manifest, queue, and credential models (`manifest.Files!`, `queue.Files!`, `entry.OutputPath!`, `entry.Length!.Value`, `queue.Credentials.ShareKey!`) after a separate validation step.
- `ShadowDrop.Api/Shares/CreateShareService.cs` dereferences request members (`request.Files!`, `request.DownloadBearerTokenExpiresAtUtc!`) after calling `ValidateRequest` (`:175`), a `void` method whose post-conditions about the *parameter's* members the compiler cannot carry to the caller.
- `ShadowDrop.Api/Uploads/MultipartUploadRequestReader.cs` is a different case despite looking similar: its `Validate` (`:151`) already returns a validated `UploadPersistenceRequest`, and both `!` (`:236-237`) sit inside `Validate` itself, in its return statement. The narrowing is lost because the guard at `:200-201` uses `String.Equals(request.EncryptionFormatVersion, …, StringComparison.Ordinal)`, which flow analysis cannot read as a null check. The fix is local — restructure the guard into a null-aware comparison or bind the checked values to non-null locals — and the same method already shows the target shape: `request.KdfSalt` needs no `!` at `:214` because `String.IsNullOrWhiteSpace` carries `[NotNullWhen(false)]`.
- `ShadowDrop.Shared/Queue/QueueFileParser.cs` uses `var validatedUri = uri!;` after an `out var` result was folded into a composite boolean.
- Single occurrences in the remaining API and CLI files should be judged individually.

### Preferred replacements

1. **Validation methods that validate a parameter's members.** Have validation *return* the proven values — a small validated projection (record or tuple of non-nullable members) that the rest of the method consumes. This is the rule for these call sites, not one option among several: no attribute can express "`request.Files` is non-null after this call". `[MemberNotNull]` states post-conditions only about members of the *containing* type, and `[NotNull]`/`[NotNullWhen]` constrain the annotated parameter itself, not its members. `CreateShareService.ValidateRequest` is the case in point — it proves `request.Files` non-null at `:177` and has no way to tell the caller. The attribute-based approach remains valid in the narrower cases where it genuinely applies: `[MemberNotNull]` on a helper that initializes the type's own fields, and `[NotNullWhen(true)]` on `TryX`-style methods that narrow a parameter or `out` result as a whole.
2. **Composite boolean checks around `out` parameters.** Restructure so the compiler sees the flow directly, e.g. `if (!Uri.TryCreate(...) || uri.Scheme is not ...) { error; return; }`, which removes the need for a re-assigned `uri!` local in `QueueFileParser`. ReSharper already flags the redundant half of that guard as `ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract` at `QueueFileParser.cs:193`.
3. **Repeated `!` on the same member.** Introduce one validated local (or deconstruct into locals) that carries the proven value through the remaining flow instead of re-applying `!` at each use.
4. **Nullable models that are never legitimately null.** Where a deserialized model member is required by contract, prefer making the model express that (required members, non-nullable properties with validation at the parse boundary) over forgiving it at every consumer. Keep such changes local to internal models; do not alter serialized wire shapes or the public API surface.

### Constraints

- No behavioral change: a removed `!` must not turn a would-be `NullReferenceException` into a different exception type or message that callers or tests depend on, and must not change validation ordering or error text.
- Do not weaken analysis: nullable stays enabled, no `#pragma warning disable` for nullable diagnostics, no `.editorconfig` severity changes for `CS86xx`.
- `build`: only a couple of occurrences; apply the same judgement, no restructuring of the Nuke targets. The `= null!` initializers on Nuke-injected members in `build/BuildPipeline.Common.cs` (`GitRepository`, `Solution`) stay — those are populated by the build framework via attribute injection, which flow analysis cannot see.

### Tests

The suite uses **FluentAssertions 7.2.2** with **NUnit 4** and **NUnit.Analyzers 4.14.0**. Both already establish non-nullness for the compiler, so a large share of the ~300 test occurrences are redundant rather than necessary:

- **FluentAssertions narrows the assertion subject.** The `Should()` extension methods annotate their subject parameter with `System.Diagnostics.CodeAnalysis.NotNullAttribute` (e.g. `ObjectAssertions Should([NotNull] this Object? subject)`). `[NotNull]` is an *unconditional* post-condition, so flow analysis treats the expression as non-null from the `Should()` call onwards and any following `!` on it is redundant. Note this is caused by `Should()` itself, not by `NotBeNull()` — the assertion is what enforces the invariant at runtime and produces a readable failure message, so keep it; only the `!` goes.
- This narrowing applies to member-access subjects, not just locals. `tests/ShadowDrop.Cli.Tests/Downloads/CliDownloadRequestFactoryTests.cs:17-18` (`request.RequestUri!.Query` after `request.RequestUri.Should().NotBeNull()`), `:28-29` (`request.Headers.Authorization!`), and `tests/ShadowDrop.Api.Tests/ApiWalkingSkeletonTests.cs:354-355` and `:432-433` are all instances of the redundant form; the recurring `RequestUri!`, `Credentials!`, and captured-request patterns follow the same rule wherever a `Should()` on that expression precedes the use. Where no assertion precedes the dereference, prefer adding `Should().NotBeNull()` over keeping the `!` — but only for values genuinely under test. Fixture-owned infrastructure is handled at its source instead (next bullet).
- **Fixture infrastructure: fix at the source, do not assert it.** The ~71 `client.BaseAddress!` occurrences in `ApiWalkingSkeletonTests`, `UploadCommandHandlerTests`, and `DownloadCommandHandlerTests` are not about the SUT — the clients come from `WebApplicationFactory<Program>` fixtures, so the base address is a framework guarantee, and asserting it would only assert ASP.NET Core's own behavior. Replace those uses with the factory's `ClientOptions.BaseAddress`, which is a non-nullable `Uri` and the very value the factory assigns to each client it creates (verified: dereferencing it without `!` compiles warning-free). Expose it through a small fixture property where that reads better at the call sites. The result needs neither `!` nor an assertion and has a single source of truth.
- Because the narrowing is unconditional, compile-time silence is not proof the value is non-null at runtime — never drop a `!` by relying on a `Should()` call that does not actually assert non-nullness.
- **NUnit.Analyzers suppresses the setup-initialization warning.** `NonNullableFieldOrPropertyIsUninitializedSuppressor` suppresses CS8618 for non-nullable fields and properties assigned in `[SetUp]`/`[OneTimeSetUp]`, so those members need neither `= null!` nor a nullable type; fields never assigned in a setup method still warn as usual. `DereferencePossiblyNullReferenceSuppressor` does the same for dereferences guarded by NUnit asserts such as `Assert.That(value, Is.Not.Null)`. Do not introduce `= null!` initializers in test fixtures; remove any that are found.
- **Preserve intentional invalid input.** `null!` arguments used to exercise argument validation (four occurrences today) are the deliberate case and must stay.

### Documentation

Two documents need updating, with no overlap in responsibility: `CODESTYLE.md` carries the general rule, `tests/AGENTS.md` carries the test-specific one so that agents writing new tests do not reintroduce redundant `!`.

#### CODESTYLE.md

Extend `CODESTYLE.md` with nullability guidance. Add it as a new section (e.g. `### Nullability`, placed near `### Error Handling`) covering:

- Nullable reference types are enabled project-wide; prefer guard clauses, validated locals, pattern matching, and nullable-flow attributes over `!`.
- Do not repeat `!` after a value has been validated — carry the proven value in one non-nullable local.
- In tests, `!` is unnecessary after a FluentAssertions `Should().NotBeNull()` on the same expression, and test fixture members assigned in `[SetUp]`/`[OneTimeSetUp]` need no `= null!` initializer because NUnit.Analyzers suppresses the warning.
- The null-forgiving operator is acceptable only for invariants flow analysis cannot establish — framework-injected members, and deliberately invalid `null!` inputs in argument-validation tests — and such uses should be obvious from the surrounding code or carry a short comment.

#### tests/AGENTS.md

Add nullability rules for writing new tests, as bullets under `## General Guidelines` (which already covers the NUnit and FluentAssertions conventions these rules depend on):

- Do not add `!` after asserting a value with FluentAssertions — `Should()` annotates its subject with `[NotNull]`, so the compiler already treats the expression as non-null afterwards. Assert with `Should().NotBeNull()` and dereference without `!`.
- Do not initialize test fixture members with `= null!` when they are assigned in `[SetUp]` or `[OneTimeSetUp]` — NUnit.Analyzers suppresses CS8618 for those; declare them non-nullable and leave them uninitialized.
- Keep `null!` only where a test deliberately passes an invalid null to exercise argument validation.

Keep this short and rule-shaped; the reasoning belongs in `CODESTYLE.md`, not here.
