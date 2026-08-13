# Resolve Recursive File Symlinks Within Upload Roots

> Issue: [#212](https://github.com/chA0s-Chris/ShadowDrop/issues/212)

## Rationale

Recursive uploads should preserve legitimate file symlinks inside the selected directory tree while preventing a discovered link from selecting a file outside that tree. This makes the recursive upload boundary match the directory the user chose without changing explicitly named file operands.

## Acceptance Criteria

- [ ] Recursive discovery resolves a discovered file symlink and selects it only when its target remains within the resolved directory-operand root.
- [ ] A discovered file symlink whose target resolves outside that root is excluded without uploading its target.
- [ ] Each excluded file symlink is named on standard error by the path found in the selected tree and counted as excluded, in both `upload` and `--dry-run`, without making the selection invalid.
- [ ] Directory symlinks continue not to be traversed, and explicitly named file operands retain their current behavior.
- [ ] `upload` and `upload raw`, including `--dry-run`, apply the same selection boundary.
- [ ] Before a non-dry-run upload opens a recursively discovered file symlink for encryption, it re-resolves the target against its resolved root and rejects a target that has moved outside that boundary.
- [ ] Automated tests cover ordinary files, in-tree file symlinks including one with a relative target, out-of-tree file symlinks, a dangling in-tree symlink, a directory operand reached through a symlinked parent directory, a directory-symlink operand that is still refused rather than expanded, the existing directory-link behavior, and the non-fatal exclusion diagnostic.
- [ ] Automated tests cover a discovered in-tree file symlink that is replaced with an out-of-tree target before the non-dry-run revalidation.
- [ ] `docs/CLI.md` and `docs/SECURITY_TRADEOFFS.md` explain the boundary and its remaining residual risk.

## Technical Details

The change belongs primarily in the existing `UploadInputResolver`, which already performs recursive discovery, applies filters, and preserves each selected source path for both normal uploads and dry runs. It should compare paths using the platform-appropriate path comparison already used for selection ordering.

The selected tree root must establish the physical comparison boundary for the directory operand. A directory-symlink operand remains refused rather than expanded, but symlinks in an ancestor of an otherwise ordinary operand must not cause a valid in-tree file link to be rejected. The root boundary and every discovered file-link target must therefore be expressed in one canonical namespace before a path-segment-boundary comparison; `ResolveLinkTarget(returnFinalTarget: true)` resolves the discovered file link, while the implementation must use a verified platform-safe strategy for making the root comparable with that final target. The displayed source path and directory-relative destination remain the path found in the selected tree.

A target that cannot be resolved — dangling, denied, or cyclic — is treated as outside the boundary and excluded through the same path as an out-of-tree target, rather than aborting the run or falling through to the later missing-file error. Unlike the directory-link case, which fails the whole run, an excluded file symlink only removes that file: a legitimate tree containing links to files outside it must still upload.

`UploadInputResolution` must carry non-fatal selection diagnostics separately from terminal input errors so handlers can count an excluded link and report its discovered path on standard error while preserving a valid plan. The normal-upload and dry-run handlers must surface the same diagnostic; JSON mode continues to emit one valid result object on standard output, with these human-readable diagnostics kept on standard error.

Recursive selections must retain the resolved-root context needed by the non-dry-run upload path to revalidate a discovered file symlink immediately before `EncryptedFileContent` opens it. A failed revalidation must stop that file before its contents are read or sent.

The current directory-link protection is intentionally path-based and documents that it can only narrow, not completely eliminate, a concurrent link-swap window with the available runtime APIs. The new revalidation is defense in depth, not a claim of handle-pinned race freedom; the documentation must retain that honest security boundary. The boundary also covers symlinks only: a hardlink or bind mount planted inside the tree is indistinguishable from an ordinary file at every API available here, and the documentation must say so rather than imply that the resolved root bounds every way a file can enter the selection.
