# workspace-native-load-cleanup-gate-release — Release the load gate after resource cleanup failure

**row:** `workspace-native-load-cleanup-gate-release` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1446`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1472`

## Acceptance

- [ ] Attempt workspace and analyzer lease cleanup independently on success/failure swap branches, preserving primary failure and resource ownership.
- [ ] Always release LoadLock in a terminal finally even if disposal throws; prove subsequent same-session reload acquires it.
- [ ] Do not copy already-landed removal/shutdown collectors or reopen completed removal work; classify any still-missing host cleanup separately.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Sequential Dispose calls precede LoadLock.Release. Current RemoveSessionEffects and ShutdownAsync already collect independent cleanup failures, so the retained broad row must narrow to the live load-finally defect.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
