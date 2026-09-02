# workspace-id-optional-adoption-evidence — Resolve the workspaceId-optional adoption gate

**row:** `workspace-id-optional-adoption-evidence` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs`

## Acceptance

- [ ] Quantify the pilot's `_meta.autoResolution` adoption signal from real usage and record a go / no-go for the full flip.
- [ ] If go, record the measured evidence and the batch plan; if no-go, record what would change the answer so the row is not re-litigated from scratch.

## Evidence

The parent row's Context says outright: "**Gate on the pilot's `_meta.autoResolution` adoption signal** before sweeping." The pilot is `workspace-id-optional-readonly-surface-flip` (PR #959, 3 tools). Measured 2026-09-02: 7 read-only tools are already optional (`SymbolTools` 3, `WorkspaceTools` 2, `CompileCheckTools` 1, plus the pilot set) against 121 still required.

## Context

Split from `workspace-id-optional-readonly-surface-full-sweep` (2026-09-02). This is the unblocking child — the flip child depends on it.
