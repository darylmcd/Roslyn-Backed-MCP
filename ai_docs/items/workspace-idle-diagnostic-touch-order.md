# workspace-idle-diagnostic-touch-order — Measure idle time before access updates

**row:** `workspace-idle-diagnostic-touch-order` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1582`

## Acceptance

- [ ] Measure previous idle duration before updating access and retain the access update.
- [ ] Use a controlled clock to prove long-idle diagnostic reachable and normal reads current.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. TouchAccess runs immediately before subtracting LastAccessedUtc, making the greater-than-60-minute branch unreachable during ordinary use.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
