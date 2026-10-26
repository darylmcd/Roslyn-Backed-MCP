# cross-project-interface-parameter-semantics — Preserve emitted interface parameter semantics

**row:** `cross-project-interface-parameter-semantics` · **pri:** `High` · **size:** `M` · **deps:** `interface-extraction-nullable-context-projection`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:619` (`CreateParameterList`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve semantic ref/out/in/params modifiers and optional defaults when generating interface parameter declarations, covering all applicable emitted member kinds.
- [ ] Use actual public extraction and dependency-inversion compilation controls; preserve valid ordinary parameters and reject only concretely unrepresentable emitted forms before token publication.
- [ ] Keep declaration/method generic identity and signature dependency ownership in their separate rows.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Independent cold audit found CreateParameterList618-624 emits only name/type, dropping method parameter modifiers/defaults. This is distinct from undeclared generic parameters and target-assembly dependency closure.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.

Current-session candidate CI at 7395d431 exposed five newly introduced unmarked server-invariant exceptions in InterfaceHandlerBindingValidator. Corrected in the same PR with InvalidOperationErrors.Internal, preserving messages/category and leaving baseline/tests unchanged; causal ratchet red: 1 failed / 87 passed, green: 88 passed / 0 failed / 0 skipped; final combined affected gate includes this source-inventory class.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
