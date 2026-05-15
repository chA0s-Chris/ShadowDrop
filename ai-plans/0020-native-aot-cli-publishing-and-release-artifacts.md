## Rationale

Finish the CLI distribution story by producing publishable binaries for the supported platforms. This slice should turn
the earlier Native AOT groundwork into a repeatable release process with concrete artifacts for Linux, macOS, and
Windows on x64 and arm64.

## Acceptance Criteria

- [ ] Native AOT publish succeeds for Linux x64.
- [ ] Native AOT publish succeeds for Linux arm64.
- [ ] Native AOT publish succeeds for macOS x64.
- [ ] Native AOT publish succeeds for macOS arm64.
- [ ] Native AOT publish succeeds for Windows x64.
- [ ] Native AOT publish succeeds for Windows arm64.
- [ ] Publish outputs are organized as release-ready CLI artifacts.
- [ ] Release artifact naming is consistent across runtime identifiers.
- [ ] Any platform-specific AOT blockers are documented, and self-contained fallback publishing is used only if a
  blocker is proven and narrowly scoped.
- [ ] README release/install documentation is updated for the CLI artifacts.

## Technical Details

Use the existing `ShadowDrop.Cli` Native AOT configuration as the baseline and extend validation to the full target
matrix from the concept. Keep the CLI implementation and dependencies trimming-safe; if a publish failure reveals an
AOT-hostile pattern, fix the code or dependency usage rather than immediately downgrading the distribution model.

Define a repeatable publish process, whether through existing build scripts or a small extension to them, that emits
clearly named artifacts per runtime identifier. Artifact layout should make it straightforward to attach binaries to
future GitHub releases or CI outputs without manual renaming.

Fallback self-contained single-file publishing is acceptable only for a platform with a confirmed AOT blocker, and that
exception should be documented explicitly rather than becoming the default. The preferred distribution model remains
Native AOT across all supported platforms.
