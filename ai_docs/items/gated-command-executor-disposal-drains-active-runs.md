# gated-command-executor-disposal-drains-active-runs — Drain owned command runs before disposing gates

**row:** `gated-command-executor-disposal-drains-active-runs` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:73`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:133`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:155`

## Acceptance

- [ ] Close admission and coordinate active/queued command ownership before disposing shared gates.
- [ ] Prove active and queued disposal deterministically; preserve cancellation/results and avoid replacement ObjectDisposedException on Release.
- [ ] Retain bounded workspace gate pruning and independent manager/provider lifetime ownership.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Dispose immediately disposes global/workspace semaphores despite ExecuteAsync finally releasing them. The same file explicitly avoids disposing a removed workspace gate while operations run, exposing inconsistent shutdown handling.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
