# client-root-widening-drive-root-parent - Decide sibling-worktree widening when the repo root's parent is a drive root

**row:** `client-root-widening-drive-root-parent` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs:114-146`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs:1019-1050`

## Acceptance

- [ ] Product/security decision recorded in ai_docs: keep the drive-root skip, or replace whole-parent widening with sibling-by-name-prefix widening (allow only `<parent>\<rootName>-*`) so a drive-root parent never grants the whole drive. Recommend the prefix form.
- [ ] Decision implemented in `GetWidenedParent`/`EnumerateAllowedRoots` with tests: `D:\repo` widens to `D:\repo-audit-x\...`; still rejects `D:\other`, `D:\repo2`, and `C:\Windows\...`. `IsPathUnderAnyRoot_Drive_Root_Never_Widens` is updated to match the decision, not weakened.
- [ ] `RootExpansionGrantRegistry` and the `ToolDispatch` apply-time revalidation (`ToolDispatch.cs:~305`) use the same widened boundary; add a test covering apply revalidation for a `D:\repo-<suffix>` workspace.
- [ ] Verify a sibling worktree `D:\<repo>-<suffix>` loads via `workspace_load(expandSanctionedRoots: true)` with `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` from a `D:\<repo>` root; confirm the surface-test flow separately (see Evidence).
- [ ] backlog: sync ai_docs/backlog.md

## Evidence

- Verified by reading code 2026-09-30: `GetWidenedParent` returns null when `Path.GetPathRoot(parent) == parent`, so a root at `D:\<repo>` (parent `D:\`) never widens. Behavior is deliberate and pinned by `IsPathUnderAnyRoot_Drive_Root_Never_Widens`.
- Since 2026-09-30 every local repo lives at `D:\<repo>` (old `C:\Code-Repo\<repo>` had a non-root parent), so sibling worktrees like `D:\<repo>-audit-...` are rejected under `expandSanctionedRoots`.
- PR #1685 moved the fixture in `IsPathUnderAnyRoot_Worktree_Subdir_Allowed_With_Flag` to `D:\Dev\TradeWise*` because a literal `D:\TradeWise` made it fail (run and observed by the agent).
- Correction to the filing premise: the `mcp-server-surface-test` disposable worktree is NOT affected as written. `skills/mcp-server-surface-test/prompts/phases/setup-and-analysis.md:39` creates it at `.worktrees/surface-test-<ts>` inside the audited root, already under the sanctioned root. Only external sibling-worktree flows (agent audit/sweep worktrees outside the repo) hit the gap.
