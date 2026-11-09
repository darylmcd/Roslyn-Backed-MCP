# interface-preview-compiler-project-detail — Remove project names from public compiler refusals

**row:** `interface-preview-compiler-project-detail` · **pri:** `Medium` · **size:** `S` · **verified:** `e1225d35ef1d69acd620dd358ed7ec7f4c664e46` · **deps:** `cross-project-public-refusals-echo-input`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/InterfacePreviewCompilationGate.cs:42`
- `tests/RoslynMcp.Tests/InterfacePreviewCompilationGateTests.cs`
- `docs/decisions/0038-interface-preview-compilation.md:15`

## Acceptance

- [ ] Preserve compiler diagnostic IDs and correction guidance while removing project names and other caller-controlled detail from public compiler-error refusals in all interface-preview routes.
- [ ] Prove a failing-before/passing-after dual-era raw-wire regression using a sensitive project-name sentinel; inspect the complete envelope, category, BCL exception type and inner-cause ownership.
- [ ] Supersede the project-name publication decision in ADR 0038, add migration guidance and apply the public breaking-change release policy.

## Evidence

- `InterfacePreviewCompilationGate.ValidateAsync` groups added diagnostics by `after.Name` and publishes each project name in `PublicInvalidOperationException`; ADR 0038 currently requires the project names. Observed during cross-project refusal remediation on 2026-11-08.

## Context

- Dependency: land `cross-project-public-refusals-echo-input` first. That change removes free-text public caller detail from cross-project and extract-and-wire refusal emitters; this row changes the distinct compiler-diagnostic presentation contract.
