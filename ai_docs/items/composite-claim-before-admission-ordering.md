# composite-claim-before-admission-ordering — Composite admission runs after the claim so a refused disk token is consumed

**row:** `composite-claim-before-admission-ordering` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`

## Acceptance

- [ ] Admission runs against the claimed record inside the claim step; a refusal leaves the token redeemable unless the record is forged; tests cover both.

## Evidence

- PR 1997 cold review follow-up 4: ContainsWorkspace and admission run after the claim.
