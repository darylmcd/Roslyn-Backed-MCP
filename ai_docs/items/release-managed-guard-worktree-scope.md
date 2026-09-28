# release-managed-guard-worktree-scope — Resolve release-managed guard paths and sentinel per worktree

**row:** `release-managed-guard-worktree-scope` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/guard-release-managed-files.ps1:57`
- `ai_docs/prompts/backlog-sweep-addenda.md:259`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs:276`

## Acceptance

- [ ] The guard resolves the repo root from the edited file (git toplevel of its directory, or the nearest `.git` file/dir), not from `$env:CLAUDE_PROJECT_DIR`, so an exact-path managed file edited inside a linked worktree (hooks.json, CHANGELOG.md, …) is blocked exactly like a primary-checkout edit.
- [ ] The override sentinel is read from that same resolved root, so a sentinel unblocks only the worktree it was created in, never every session/worktree on the machine.
- [ ] The addenda § Hooks text (and workflow.md § Release-managed file guard if it repeats the location) states where the sentinel goes: the root of the worktree being edited.
- [ ] Tests run the script against a worktree-shaped path: exact-path managed file blocked without sentinel, allowed with a worktree-root sentinel, still blocked when only a primary-root sentinel exists.

## Evidence

- HEAD 6f31f065 `guard-release-managed-files.ps1:57`: `$repoRoot = $env:CLAUDE_PROJECT_DIR`; `:98`: `$sentinel = Join-Path $repoRoot '.release-managed-edit-allowed'` — the sentinel is always read from the session's project root (the primary checkout for worktree executors).
- `:61-66`: the relative path is computed by stripping `$repoRootNorm + '/'`; a worktree edit becomes `.worktrees/<id>/hooks/hooks.json`, which is not in `$managedExact` (`'hooks/hooks.json'`, `'changelog.md'`, ...), so exact-path managed files edited in a worktree pass unguarded (`if (-not $isManaged) { exit 0 }`). Only the basename rule (`bannedsymbols.txt`) still fires there — and then needs a primary-root sentinel, which unblocks every worktree for its 1800 s TTL.
- Addenda `:259-261`: "an initiative that intentionally edits it must first create the fresh `.release-managed-edit-allowed` sentinel" — no location given.
- Existing row detail `argument-exception-public-message-guard` already works around this: "uses the primary-root .release-managed-edit-allowed sentinel (where the guard reads it)".

Source: backlog-remediate 20260926T234932Z follow-up.
