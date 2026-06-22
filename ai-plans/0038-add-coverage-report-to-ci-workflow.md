## Rationale

Expose the merged test coverage produced by the NUKE `Test` target directly in GitHub Actions and on pull requests so
coverage changes are visible without downloading build artifacts. The workflow should publish a job summary and keep
a single coverage comment updated for each pull request. This plan addresses
[#38](https://github.com/chA0s-Chris/ShadowDrop/issues/38).

## Acceptance Criteria

- [ ] The CI workflow creates a Markdown coverage summary from the merged Cobertura report at
  `artifacts/test-coverage/coverage.cobertura.merged.xml`, including a badge and the `60 85` thresholds.
- [ ] The coverage summary is written to the GitHub Actions job summary after the NUKE `Test` target completes.
- [ ] Same-repository pull-request runs create or update a single sticky comment containing the coverage summary,
  including pull requests created by Renovate; fork, Dependabot, and push runs do not attempt to create a pull-request
  comment.
- [ ] The workflow grants only the GitHub token permissions required to read repository contents and write
  pull-request comments.
- [ ] The updated workflow passes local YAML and GitHub Actions syntax validation.

## Technical Details

Extend `.github/workflows/ci.yml` after the existing `Run NUKE pipeline` step. Use
`irongut/CodeCoverageSummary@v1.3.0` with the repository's actual merged report path, Markdown output, `output: both`,
the coverage badge enabled, and thresholds set to `60 85`. In a following step, append the generated
`code-coverage-results.md` to the GitHub Actions job summary with `cat code-coverage-results.md >> $GITHUB_STEP_SUMMARY`
so the coverage report is visible on every run regardless of event type. Then use
`marocchino/sticky-pull-request-comment@v3` to publish the same `code-coverage-results.md` file as a sticky comment.
Guard the comment step so it runs only for pull requests whose head repository matches the current repository and
whose actor is not `dependabot[bot]`. This allows standard same-repository Renovate pull requests to receive coverage
comments while fork, Dependabot, and `main` push runs retain the job summary without attempting to comment. Add
`pull-requests: write` alongside the existing `contents: read` job permission. After the implementation is committed,
pushed, and opened as a pull request, verify that CI writes the report to the job summary and that a subsequent run
updates the existing coverage comment instead of creating a duplicate; this live verification is a post-PR follow-up
and is not part of `implement-plan` completion.
