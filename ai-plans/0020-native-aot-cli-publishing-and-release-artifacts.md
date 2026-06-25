## Rationale

This plan is abandoned because the Native AOT CLI publishing and release-artifact workflow was completed through
[#69](https://github.com/chA0s-Chris/ShadowDrop/issues/69). The original `#20` design assumed a single NUKE publish flow
could build every CLI runtime flavor, but implementation showed that Native AOT publishing needs platform-specific
runner toolchains. The replacement work in `#69` uses separate Linux, macOS, and Windows publish targets plus a GitHub
workflow that aggregates the final release artifacts and checksums.

## Acceptance Criteria

- [x] Plan abandoned in favor of `#69`.
- [x] Native AOT CLI publishing for all supported runtime identifiers is covered by the `#69` publish targets and
  release-artifacts workflow.
- [x] `#20` issue closed as superseded by `#69`.

## Technical Details

No implementation should be done from this plan. Use
`ai-plans/0069-publish-nuke-targets.md` for the implemented release-artifact workflow and publish-target design.
