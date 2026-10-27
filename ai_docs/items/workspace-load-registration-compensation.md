# workspace-load-registration-compensation — Compensate failed watcher registration

**row:** `workspace-load-registration-compensation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:469`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:495`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:503`

## Acceptance

- [ ] After post-registration failure remove only the exact session, unwatch/dispose owned resources and return capacity.
- [ ] Preserve primary failure/cancellation plus cleanup failures and unrelated/replacement sessions.
- [ ] Prove a real watcher-throw old-red and a subsequent load using the released slot; do not require proposal-only fork invocation APIs.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. sessionAdded is set before Watch; catch disposes session but does not remove it, while finally only releases slots when sessionAdded=false.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
