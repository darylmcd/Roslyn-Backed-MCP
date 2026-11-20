# interface-preview-compiler-project-detail — Remove project names from public compiler refusals

**row:** `interface-preview-compiler-project-detail` · **pri:** `Medium` · **size:** `S` · **deps:** `cross-project-public-refusals-echo-input`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/InterfacePreviewCompilationGate.cs:16-22`
- `tests/RoslynMcp.Tests/InterfacePreviewCompilationGateTests.cs`
- `docs/decisions/0038-interface-preview-compilation.md:15`

## Acceptance

- [ ] Preserve compiler diagnostic IDs and correction guidance while removing project names and other caller-controlled detail from public compiler-error refusals in all interface-preview routes.
- [ ] Prove a failing-before/passing-after dual-era raw-wire regression using a sensitive project-name sentinel; inspect the complete envelope, category, BCL exception type and inner-cause ownership.
- [ ] Supersede the project-name publication decision in ADR 0038, add migration guidance and apply the public breaking-change release policy.

## Evidence

- `InterfacePreviewCompilationGate.ValidateAsync` groups added diagnostics by `ProjectName` and publishes each project name in `PublicInvalidOperationException`; ADR 0038 currently requires the project names. Observed during cross-project refusal remediation on 2026-11-08.

## Context

- Dependency: land `cross-project-public-refusals-echo-input` first. That change removes free-text public caller detail from cross-project and extract-and-wire refusal emitters; this row changes the distinct compiler-diagnostic presentation contract.

## Notes

5.x screen at d9e2b3ff. The three current consumers are experimental, but ADR 0038 mandates each affected project name and this row requires the deferred cross-project refusal migration. Reopen the chain at the next operator-approved major. Revalidate the public contract and cover both raw protocol eras when superseding the ADR.

Operator decision 2026-11-20: its dep cross-project-public-refusals-echo-input is unparked as a 5.x security correction; this row follows it in 5.x under the same rule (ADR 0038 supersession and migration note still required).
