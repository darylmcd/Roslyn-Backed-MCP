# architecture-workspace-id-optional-pilot-invariant — Align the architecture invariant with the optional pilot

**row:** `architecture-workspace-id-optional-pilot-invariant` · **pri:** `Low` · **size:** `M` · **deps:** `workspace-id-optional-adoption-evidence`

## Anchors

- `ai_docs/architecture.md`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs`

## Acceptance

- [ ] Replace the absolute “must be passed on all workspace operations” statement with the live explicit-or-safe-auto-resolution contract.
- [ ] Keep mutation/version-safety requirements explicit and link the adoption decision that governs further optional rollout.
- [ ] Add or extend an AI-doc verification assertion so the stale absolute invariant cannot return.

## Evidence

`ai_docs/architecture.md` says every workspace operation must receive `workspaceId`, while the live public surface intentionally makes it optional for the pilot methods in `SymbolTools` and `CompileCheckTools`. The mismatch can misdirect maintainers and agents.

## Context

Observed during the 2026-09-04 remediation of `workspace-id-optional-adoption-evidence`. Resolve after that judgment-heavy gate records the authoritative go/no-go posture.
