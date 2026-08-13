# Resolve Recursive File Symlinks Within Upload Roots

> Issue: [#212](https://github.com/chA0s-Chris/ShadowDrop/issues/212)

## Rationale

Recursive uploads should preserve legitimate file symlinks inside the selected directory tree while preventing a discovered link from selecting a file outside that tree. This makes the recursive upload boundary match the directory the user chose without extending that boundary to explicitly named file operands.

## Acceptance Criteria

- [x] Recursive discovery resolves a discovered file symlink and selects it only when its target remains within the resolved directory-operand root.
- [x] A discovered file symlink whose target resolves outside that root is excluded without uploading its target.
- [x] Each excluded file symlink is named on standard error by the path found in the selected tree, in both `upload` and `--dry-run`, and is counted as excluded without failing a run that still selects files; a run left with no files reports those exclusions alongside its empty-selection error.
- [x] Directory symlinks continue not to be traversed, and explicitly named file operands remain outside the recursive boundary.
- [x] `upload` and `upload raw`, including `--dry-run`, apply the same selection boundary.
- [x] Before a non-dry-run upload opens a recursively discovered file for encryption, it re-resolves that file against its resolved root and rejects one that now resolves outside the boundary, whether or not it was a link when it was discovered.
- [x] A selected file symlink is uploaded with the plaintext length, chunk count, and final-chunk marking of its target rather than of the link, and automated tests cover both planning and encryption for such a link.
- [x] Automated tests cover ordinary files, in-tree file symlinks including one with a relative target, out-of-tree file symlinks, a dangling in-tree symlink, a directory operand reached through a symlinked parent directory, a directory-symlink operand that is still refused rather than expanded, the existing directory-link behavior, and the non-fatal exclusion diagnostic.
- [x] Automated tests cover the path-canonicalization cases the boundary depends on: a link whose resolved target is reached through linked ancestors, a target reached by `..` through an in-tree directory link, and a link that cannot be resolved because it is cyclic.
- [x] Automated tests cover a discovered in-tree file symlink that is replaced with an out-of-tree target before the non-dry-run revalidation.
- [x] `docs/CLI.md` and `docs/SECURITY_TRADEOFFS.md` explain the boundary and its remaining residual risk.

## Technical Details

The change belongs primarily in the existing `UploadInputResolver`, which already performs recursive discovery, applies filters, and preserves each selected source path for both normal uploads and dry runs. It should compare paths using the platform-appropriate path comparison already used for selection ordering.

The selected tree root must establish the physical comparison boundary for the directory operand. A directory-symlink operand remains refused rather than expanded, but symlinks in an ancestor of an otherwise ordinary operand must not cause a valid in-tree file link to be rejected. The root boundary and every discovered file-link target must therefore be expressed in one canonical namespace before a path-segment-boundary comparison. Producing that namespace means resolving each path one component at a time, following every link encountered and re-resolving the ancestry of whatever it points at, bounded by a hop limit that ends a cycle. Two shortcuts are known to break the comparison and must be avoided: collapsing `..` as text steps back out of the directory a link appears to sit in rather than the one it actually points at, and resolving only a link's own final target leaves the links in that target's ancestry unresolved, so two spellings of one location do not compare equal. The
displayed source path and directory-relative destination remain the path found in the selected tree.

A target that cannot be resolved — dangling, denied, or cyclic — is treated as outside the boundary and excluded through the same path as an out-of-tree target, rather than aborting the run or falling through to the later missing-file error. Unlike the directory-link case, which fails the whole run, an excluded file symlink only removes that file: a legitimate tree containing links to files outside it must still upload.

`UploadInputResolution` must carry non-fatal selection diagnostics separately from terminal input errors so handlers can count an excluded link and report its discovered path on standard error while preserving a valid plan. Those diagnostics must survive an invalid resolution as well, so a run whose only candidates were excluded still names them. The normal-upload and dry-run handlers must surface the same diagnostic; JSON mode continues to emit one valid result object on standard output, with these human-readable diagnostics kept on standard error.

Recursive selections must retain the resolved-root context needed by the non-dry-run upload path to revalidate every recursively discovered file immediately before `EncryptedFileContent` opens it, so a file that was ordinary at discovery is covered as well. A failed revalidation must stop that file before its contents are read or sent.

The current directory-link protection is intentionally path-based and documents that it can only narrow, not completely eliminate, a concurrent link-swap window with the available runtime APIs. The new revalidation is defense in depth, not a claim of handle-pinned race freedom; the documentation must retain that honest security boundary. The boundary also covers symlinks only: a hardlink or bind mount planted inside the tree is indistinguishable from an ordinary file at every API available here, and the documentation must say so rather than imply that the resolved root bounds every way a file can enter the selection.

Plaintext length must be measured through an open handle rather than from `FileInfo.Length`, which reports a symlink's own size instead of its target's. The planner, the pre-reservation revalidation, and the chunk count that marks the final chunk all derive from that measurement, so a link would otherwise promise the length of its stored target path and send a payload that neither satisfies its own `Content-Length` nor decrypts.
