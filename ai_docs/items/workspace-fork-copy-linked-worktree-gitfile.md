# workspace-fork-copy-linked-worktree-gitfile — Exclude linked-worktree Git pointer files from fork copies

**row:** `workspace-fork-copy-linked-worktree-gitfile` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:447-482`
- `tests/RoslynMcp.Tests/WorkspaceForkApplyCancellationTests.cs:58-78`

## Acceptance

- [ ] Exclude regular `.git` files as well as `.git` directories when copying fork source trees.
- [ ] Red-first: create a source containing a `.git` pointer file and ordinary source files; the fork contains the source files but no `.git` pointer.
- [ ] Preserve directory exclusions, secret-file filtering, link filtering, and cancellation checks.

## Evidence

- CopyDirectory's directory loop checks DirectoryCopyExclusions, which includes `.git`.
- Its file loop checks only IsReparsePoint and IsSecretBearingFile; a regular `.git` pointer is neither, so File.Copy includes it.
- A copied linked-worktree pointer references the source's existing Git metadata instead of establishing independent fork metadata.

## Context

- Found during gated-validate-test-phase-budget review on base 2047634e.
- Separate mechanism: file-copy exclusion policy; timeout and source-lock changes do not introduce or modify that policy.
