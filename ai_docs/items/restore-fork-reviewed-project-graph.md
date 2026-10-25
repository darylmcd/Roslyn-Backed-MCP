# restore-fork-reviewed-project-graph — Replay the reviewed project graph before fork restore

**row:** `restore-fork-reviewed-project-graph` · **pri:** `Medium` · **size:** `M` · **deps:** `fork-evaluated-source-isolation,document-set-project-file-undo-capture`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:128`
- (new) `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:835`
- (new) `src/RoslynMcp.Roslyn/Services/RestoreProjectGraph.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/ForkGraphPreparationTests.cs`

## Acceptance

- [ ] A representable ordinary reference-changing preview rebases onto current source state, captures source roots before copy, replays the existing shared physical renderer and document changes, and verifies evaluated fork references/compile inventory against that intended snapshot before restore; source bytes/version/token remain authoritative for later redemption.
- [ ] Refuse concrete unrepresentable snapshots before creating/copying a fork; retain current sibling edits, metadata/encoding, rollback and accepted ordinary graph changes. Do not blanket-refuse representable references.
- [ ] Observe real producer graph divergence fail before repair, compare reviewed physical changes to fork/source graphs and later source redemption, preserve landed apply/undo/rebase/diff regressions, and pass fresh review plus full serialized producer and hosted Windows/Linux/SDK-floor gates before closure.

## Scope

- At most four production files, three test files and one fragment. Replay and fidelity verification land together as one snapshot-projection contract.
- Source isolation and document encoding are merged dependencies; preserve their protection.
- Use existing rebase logic with an accessibility change only; do not copy the old RefactoringService or renderer over current main.

## Evidence

- restore-fork-red.log: the public cross-project producer's reviewed reference was missing from the actual copied graph before restore.
- Prior source checkpoint b88d8c and origin/codex/restore-reviewed-graph/stash are read-only references. PR1806 and PR1809 stay untouched; no native-authority machinery is required by this row.
- A demonstrated producer namespace failure requires its own S row before this row; do not silently add that separate production mechanism.
