## Rationale

Raise overall test coverage to about 95% line coverage. Generated source-generator output (the JSON serializer
context, regex generators, and other `obj/` files) is now excluded from the coverage report via `coverlet.xml`, so the
merged metric finally matches what dotCover reports in Rider. Against that corrected baseline the suite of 272 green
tests sits at roughly 81.6% line and 76.3% branch coverage (ShadowDrop.Shared about 95%, ShadowDrop.Api about 87%, and
ShadowDrop.Cli about 73%). The remaining gap is dominated by error-handling branches and a handful of fully untested
helper and console-UX classes rather than by gaps in the core happy paths, so closing it is almost entirely additive:
new unit and integration tests against existing seams (`FakeInteractiveSession`, the `WebApplicationFactory`-based CLI
fixture, and a fake `HttpMessageHandler`) without any production refactoring. Branch coverage is the weakest metric and
the primary target, because most uncovered code sits in `catch` and validation branches. The ShadowDrop.Cli assembly,
now correctly measured at about 73% once its generated serializer context is excluded, needs the most attention. This
plan addresses [#40](https://github.com/chA0s-Chris/ShadowDrop/issues/40).

## Acceptance Criteria

- [ ] Merged line coverage (with generated code already excluded) reaches at least 95%, and the CI coverage thresholds
  in `.github/workflows/ci.yml` (currently `60 85`) are updated to `75 90` (a soft floor at 75% and a healthy mark at
  90%). Reaching 95% is the goal for this work but is intentionally not enforced as a hard gate going forward.
- [x] Quick wins: `CliConfigPathResolver` is covered (HOME set, HOME empty falling back to `UserProfile`, and both
  empty returning `null`), `EnvironmentReader` is covered, and the `CliApplicationServices` secondary constructors and
  `CreateDefault` are exercised.
- [ ] Interactive handlers: `InteractiveDownloadCommandHandler` and `InteractiveUploadCommandHandler` have direct unit
  tests driving them through `FakeInteractiveSession` (cancellation, validation rejections, multi-file selection,
  expiration choices, and error display), each reaching at least 95%.
- [ ] CLI download error paths: `DownloadCommandHandler` reaches at least 95%, covering the `--queue` combined-with-
  share-id conflict, missing share id, invalid/short/non-hex share keys, key-file read failures, base64/URL/format
  errors, queue-entry mismatch, and the `IOException`, `UnauthorizedAccessException`, `HttpRequestException`,
  `CryptographicException`, and `TaskCanceledException` catch branches.
- [ ] API upload and stream validation: `MultipartUploadRequestReader` and the `DownloadFileService` inner streams
  (`LengthLimitingReadStream`, `SkipAsync`, `MaxLengthReadStream`, `ValidatingMultipartContentStream`) cover the
  malformed-input, length-mismatch, and truncation branches.
- [x] HTTP client error handling: `CreateShareApiClient` and `ShareManifestClient` are tested against non-success
  status codes and malformed or empty response bodies.
- [ ] Parser and stream remainders: the remaining branches in `CliDownloadResponseParser`, `QueueFileParser`,
  `UploadApiClient`, `CliDownloadSession`, and `LiteDbUploadedFileMetadataRepository` are closed.
- [ ] Remaining API and Shared gaps are closed so the overall 95% goal is met, including `AdminEndpoints`,
  `LocalBlobStorage`, `UploadPersistenceService`, `CreateShareService`, `ShadowDropOptionsBinding`,
  `FileSystemAccessPermissions`, `ChunkEncryptionService`, and the small crypto types (`ContentKey`, `ShareSecret`,
  `ChunkRange`, `ChunkMetadata`).
- [x] `SpectreCliInteractiveSession` (genuine hand-written console I/O, about 13% covered) is covered via the
  `Spectre.Console.Testing` package driving a `TestConsole`; because the 95% target leaves no room to leave a real
  class untested, excluding it from coverage is not an acceptable substitute here. (A minimal internal
  `SpectreCliInteractiveSession(IAnsiConsole)` constructor was added as a test seam.)
- [ ] Any production code excluded from coverage uses `[ExcludeFromCodeCoverage]` only on branches that are provably
  unreachable through tests, each with a short justifying comment; this is limited to genuinely-unreachable defensive
  code and is never used for `SpectreCliInteractiveSession` or for reachable-but-untested code.
- [ ] Automated tests are written and the full suite stays green.

## Technical Details

Because the gap is dominated by branches, tests should target inputs that trigger each `catch` and validation path
rather than adding more happy-path assertions.

Reuse the existing seams. The CLI handlers already take `HttpClient`, `Stream`, and `TextWriter` constructor
arguments, so the existing hand-rolled fake `HttpMessageHandler` pattern already used in the CLI tests (for example in
`CliDownloadSessionTests`, `UploadApiClientTests`, and `UploadCommandHandlerTests`) can be reused to drive the error
responses for `ShareManifestClient`, `CreateShareApiClient`, and `UploadApiClient` rather than introducing a new fake.
The interactive handlers already accept `ICliInteractiveSession`;
extend the existing `FakeInteractiveSession` response queues as needed and assert on its `Errors`, `Messages`, and
`Summaries` collections instead of introducing new fakes. For `DownloadCommandHandler` error branches, prefer the
existing `CliDownloadApiFactory` fixture for end-to-end paths and the fake handler for transport faults; key-file
paths can use real temporary files, with a missing or non-readable path exercising the `IOException` and
`UnauthorizedAccessException` branches.

For the API streams, `MultipartUploadRequestReader` and the nested `DownloadFileService` streams are best exercised by
feeding crafted `MemoryStream` and multipart bodies that are too short, too long, or malformed. Many branches are
reachable directly through the reader without hosting the full application, which keeps these tests fast.

Generated source-generator output is already excluded from coverage by the committed `coverlet.xml` change
(`ExcludeByAttribute` for `GeneratedCodeAttribute`/`CompilerGeneratedAttribute`/`ExcludeFromCodeCoverageAttribute` plus
`ExcludeByFile` for `**/obj/**/*.cs`), so no further filter work is needed and every remaining line in the report is
hand-written code that the new tests are expected to reach.

`SpectreCliInteractiveSession` is pure console I/O at about 13% coverage. Add a `PackageVersion` entry for
`Spectre.Console.Testing` in `Directory.Packages.props` at the latest version (currently 0.57.0), referenced from the
CLI test project, and bump the existing `Spectre.Console` entry to the same version so the runtime and testing packages
stay aligned under central package management. Drive a `TestConsole` with scripted input to cover the confirmation,
text, single- and multi-selection prompts, the secret and validation branches of `PromptText`, and the
`ShowError`/`ShowMessage`/`ShowSummary` rendering paths. The `IsInteractiveSupported` property reads console redirection
state and can be asserted under redirected output. Because the 95% target leaves no slack for an untested production
class, the previously-considered option of excluding this class from coverage is rejected.

For genuinely-unreachable defensive branches that tests cannot drive, a targeted `[ExcludeFromCodeCoverage]` with a
short justifying comment is acceptable to keep the 95% goal attainable. This is a last resort for provably-unreachable
code only; it must never be used to hide reachable-but-untested code, and it does not apply to
`SpectreCliInteractiveSession`.

Suggested sequencing: do the quick wins first for a fast confidence boost, then the interactive handlers and the CLI
download error paths, which together account for the largest absolute gains (roughly 240 uncovered lines). Reaching
95% then requires completing the remaining packages as well — the API stream validation, the HTTP client error
handling, `SpectreCliInteractiveSession`, and the parser/stream remainders are all needed to clear the bar rather than
being optional buffer, since at 95% there is little room for any class to lag.
