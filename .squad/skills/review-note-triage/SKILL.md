---
name: "review-note-triage"
description: "Assess PR review notes against live code and binding plan language"
domain: "review"
confidence: "high"
source: "nate"
---

## Pattern

1. Pull the live unresolved PR threads first; stale local context is not enough.
2. Verify the claim directly in the current code and tests.
3. Cross-check any plan-based claim against the plan wording:
  - `must`, `shall`, acceptance criteria = binding
  - `may`, `consider`, `e.g.` = advisory
4. Classify each note separately:
  - **Valid**: real bug/spec violation with actionable fix
  - **Partially valid**: real improvement or edge case, but overstated severity or not strictly required
  - **Not actionable**: incorrect premise, already fixed, or outside accepted contract
5. For retry/error-handling notes, test the final-attempt path, not just earlier retries.
