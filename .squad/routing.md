# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Scope, architecture, priorities | Nate | Feature slicing, API contracts, sequencing work, reviewer decisions |
| API, storage, metadata workflows | Eliot | Upload/download endpoints, share lifecycle, repositories, backends |
| CLI and terminal UX | Sophie | Command shape, config handling, queue files, Spectre.Console flows |
| Security and crypto design | Alec | Token handling, encryption format, threat review, exposure boundaries |
| Platform and delivery | Tara | Docker image, CI/CD, cross-platform publishing, Native AOT validation |
| Code review | Nate | Review PRs, check quality, suggest improvements |
| Testing | Parker | Write tests, find edge cases, verify fixes |
| Scope & priorities | Nate | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |
| Backlog and issue monitoring | Ralph | Check assigned work, PR state, CI failures, ready-to-merge items |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Nate |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Nate** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Review Gate (Pre-User Review)

All changes to production code or significant feature work must pass an internal review gate before reaching the user for final approval.

**Default Review Pair:** Nate (Lead) + Parker (Tester)

**Security-Sensitive Review:** When work touches any of the following areas, Alec (Security Engineer) **must** also review before user approval:
- Authentication and token handling
- Encryption and cryptographic operations
- Secret storage, derivation, or cleanup
- Permission boundaries and access control
- Threat-relevant design or data flow

**Process:**
1. Developer completes work and stages for review.
2. Nate performs architecture and quality review. Parker performs test coverage and correctness review.
3. If security-sensitive: Alec performs threat and boundary review.
4. Only after all reviewers approve does the work proceed to user review.
5. Reviewer rejection follows the `reviewer-protocol` SKILL: lockout of the original author, reassignment to a different agent, or escalation.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
