# change-signature-preview-latency — Bring change_signature_preview under 30 s on RoslynMcp.slnx

**row:** `change-signature-preview-latency` · **pri:** `High` · **size:** `M` · **verified:** `f7016d6c5fb4b75abc695d75fcfed58d3eeebb9b`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:292`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:1186`
- `tests/RoslynMcp.Tests/ChangeSignatureBindingsTests.cs`

## Acceptance

- [ ] A failed `change_signature_preview` returns well inside the writer budget: on `RoslynMcp.slnx` an op=add with an unknown `parameterType` is refused in under 30 s (56.3 s measured 2026-11-21 at f7016d6c after PR1; the audit saw 14-48 s).
- [ ] A successful add/remove/reorder on `PhysicalPathResolver.CanonicalizeDriveRoot` returns in under 30 s (67.2 s measured).
- [ ] Every refusal and diagnostic-evidence check covered by the existing `ChangeSignature*` suites still fires (no weaker verification); before/after seconds recorded in the row note.

## Evidence

- Measured with a throwaway `ChangeSignatureTestBase` harness + Stopwatch on the slnx (cold): refusal (CS0246 at `ValidateCandidateAsync`) 56.3 s; success 67.2 s. Baseline `GetDiagnostics` (`:292-300`), the scan binding and the candidate diagnostics (`:1186`) are three full bindings.
- Split from `change-signature-unrelated-invocation-rewrite` bullet 4.

## Context

The baseline diagnostics pass runs before any scan, so it cannot move without restructuring `BuildAsync`, which `change-signature-builder-decompose` and `change-signature-preview-builder-build-async-decomposition` also rewrite: sequence with them. Cut passes without dropping a check.
