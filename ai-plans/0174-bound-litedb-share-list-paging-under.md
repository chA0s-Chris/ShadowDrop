# Bound LiteDB share-list paging under lifecycle filters

> Issue: [#174](https://github.com/chA0s-Chris/ShadowDrop/issues/174)

## Rationale

`LiteDbShareMetadataRepository.GetListPageAsync` assembles a page one equal-creation-timestamp group at a time, issuing a targeted lookup per distinct timestamp. Under the `active` predicate LiteDB's planner leaves the creation-time index, so every one of those lookups costs a full predicate evaluation: a `--status active --page-size 200` request against 10 000 shares takes roughly 3.2 seconds and grows linearly with the collection, while every other filter shape stays flat and the MongoDB path is one indexed query.

Make the per-page work a bounded number of provider queries on the default metadata provider, without changing the share-list contract, ordering, cursor format, or any observable result.

## Acceptance Criteria

- [ ] Return the same shares in the same order as canonical descending `(CreatedAtUtc, ShareId)` ordering for every combination of lifecycle filter, page size, and cursor position, identically on LiteDB and MongoDB.
- [ ] Serve a LiteDB page with at most three provider queries, a count that grows with neither page size nor the number of distinct creation timestamps the page spans, under every filter combination.
- [ ] Cover that query bound with automated assertions for page sizes `1`, `50`, and `200`, unfiltered and under `active`, on a first page, a cursored page, and a page whose cursor sits inside an equal-creation-timestamp group.
- [ ] Hold at most `pageSize + 1` share documents at once and request at most `pageSize + 1` per provider query, and keep
  `CountMatchingAsync` materializing none.
- [ ] Cover paging across an equal-creation-timestamp group larger than one page, including a cursor positioned inside such a group, for both LiteDB and MongoDB.
- [ ] Cover a tie group straddling the `pageSize + 1` window boundary, filtered and unfiltered, so a truncated trailing group can neither drop nor duplicate a share.
- [ ] Keep `GET /api/admin/status` counts and share-list totals in agreement for one `nowUtc`.
- [ ] Update the LiteDB paging note in `docs/DEPLOYMENT.md` to describe the bounded per-page query cost and the residual per-query scan under a lifecycle filter.
- [ ] Report before/after `--status active` timings at `pageSize=200` in the pull request description.

## Technical Details

Replace the group walk with a document window plus at most two targeted group queries:

1. **Cursor group** — only when a cursor is present: `CreatedAt = cursor.CreatedAt AND $._id < cursor.ShareId`, ordered by
   `ShareId` descending, limited to `pageSize + 1`. The window cannot resolve this group in memory, because LiteDB orders by one field and the window's members of a tie group are an arbitrary subset rather than the ordered continuation.
2. **Window** — `CreatedAt < cursor.CreatedAt` (unbounded on a first page), ordered by `CreatedAt` descending, limited to the remaining need.
3. **Trailing group** — only when the window came back full: re-query its oldest timestamp with `CreatedAt = T`, ordered by
   `ShareId` descending, limited to the remaining need.

Correctness rests on the window being a total sort on `CreatedAt`: no row of an older timestamp can appear before a row of a newer one, so every group in the window except the oldest is complete and orders in memory by canonical lower-case UUID `D`
ordinal comparison. The oldest group is truncated exactly when the window hit its limit; a short window means the query is exhausted and the page is final. Discard the truncated trailing rows before issuing query 3, so peak retention stays within
`pageSize + 1` — the trailing rows are read twice, which is the deliberate cost of the bound.

In-memory tie ordering must stay identical to the LiteDB-side `OrderByDescending(ShareId)` and the `$._id <` continuation predicate that queries 1 and 3 still use. #171 established and pinned that equivalence; the existing boundary-identifier tests keep it honest.

`FindCreatedAtBatch` and the now-unused `createdAtOrBefore` parameter of `CreateListQuery` go away. Forcing the creation-time index by dropping the lifecycle predicate from the group queries and filtering in memory was considered and rejected: a tie group is unbounded, so it would trade the query bound for an unbounded materialization.

Prove the query bound with a fourth optional `Action<Int32>?` test hook on the existing internal constructor, invoked once per provider query `GetListPageAsync` issues and receiving that query's limit, so both the query count and the per-query bound are asserted rather than inspected — the same pattern as `_afterInsertTestHook` and `_statusStatsIterationTestHook`. Timings for the pull request come from a throwaway harness and are not committed.

The bound is on query count, not on time: the cursor-group and trailing-group queries still evaluate the lifecycle predicate without the creation-time index, so a page stays linear in collection size with a constant of roughly two queries instead of two hundred. Expect a `--status active` page at `pageSize=200` over 10 000 shares to land in the tens of milliseconds against the measured 3.2 seconds; a result still in the hundreds means the group walk was not fully removed.

Extend `LiteDb_ShouldPageThroughLargeEqualTimestampGroup_WithoutGapsOrDuplicates` in
`tests/ShadowDrop.Api.Tests/Shares/ShareListRepositoryTests.cs` rather than replacing it, adding a group sized to straddle the
`pageSize + 1` window. Extend the tied-group assertions in `MongoPersistenceIntegrationTests` (`MongoIntegration` category) to a full cursor walk through a group larger than one page. The MongoDB implementation, the share-list contract, filters, cursor format, CLI surface, and `CountMatchingAsync` are unchanged.
