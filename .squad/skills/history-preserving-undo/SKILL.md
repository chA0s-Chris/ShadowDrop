---
name: "history-preserving-undo"
description: "Rewrite a local branch to drop commits from history while keeping their file changes available for review"
domain: "version-control"
confidence: "high"
source: "earned"
tools:
  - name: "bash"
    description: "Run the local git history rewrite and validation commands"
    when: "Use when a branch must lose commits without losing their file changes"
---

## Context
Use this when a local branch contains one or more commits that should disappear from branch history, but the file changes from those commits must remain in the worktree for human review. It is especially useful when later metadata or `.squad/` commits should stay committed while feature/code commits become uncommitted again.

## Patterns
1. Identify the last commit you want to drop and the base commit that should remain below the preserved tail.
2. Rebase the trailing commit range onto the safe base with `git rebase --onto <base> <last-dropped-commit>`. This keeps later commits in history while removing the target commits from the branch lineage.
3. Reapply the dropped commits with `git cherry-pick --no-commit <oldest> <newest>` so their content returns as uncommitted review material.
4. Validate with `git log --oneline`, `git status --short --branch`, and `git rev-list --left-right --count HEAD...origin/<branch>` to confirm the history rewrite and branch divergence.
5. If safety matters, create a local backup branch before rewriting so the original tip remains recoverable without touching the remote.

## Examples
```bash
git branch backup/pre-undo HEAD
git rebase --onto afb29a7 546e543
git cherry-pick --no-commit ff9b8ff 546e543
git status --short --branch
git log --oneline --decorate -n 5
```

In ShadowDrop issue #2 recovery, this preserved the rewritten `docs(squad)` commit in history while surfacing `src/ShadowDrop.Shared/Crypto/*` and `tests/ShadowDrop.Shared.Tests/Crypto/*` as staged changes for review.

## Anti-Patterns
- Using `git reset --hard` for this workflow — it risks discarding user changes.
- Dropping commits without a backup ref when the user asked for the safest local rewrite.
- Recommitting the restored feature changes immediately when the goal is to leave them reviewable as uncommitted work.
- Forgetting to verify ahead/behind state against origin after the rewrite.
