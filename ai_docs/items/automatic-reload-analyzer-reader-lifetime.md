# automatic-reload-analyzer-reader-lifetime — Exclude readers before automatic reload retires providers

**row:** `automatic-reload-analyzer-reader-lifetime` · **pri:** `High` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:242`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:454`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:580`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1453`

## Acceptance

- [ ] Re-derive automatic stale reload under the existing workspace writer exclusion without reentrant deadlock.
- [ ] Hold an actual provider/generator reader while another stale invocation reloads; retain its dependency resolution until release.
- [ ] Preserve explicit reload/close/eviction and request metrics/deadlines; observe causal old-red/new-green.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. ApplyStalenessPolicyAsync calls ReloadAsync before acquiring the per-workspace gate; manager reload uses its load lock and disposes prior workspace/analyzer lease. That load lock does not exclude a gate-held semantic reader.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
