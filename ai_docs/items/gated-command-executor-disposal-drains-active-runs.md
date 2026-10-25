# gated-command-executor-disposal-drains-active-runs — Drain owned command runs before disposing gates

**row:** `gated-command-executor-disposal-drains-active-runs` · **pri:** `Medium` · **size:** `M` · **deps:** —

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
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
