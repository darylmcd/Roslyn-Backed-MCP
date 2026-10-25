# compile-check-diagnostic-suppressor-parity — Investigate suppressor parity with native compiler results

**row:** `compile-check-diagnostic-suppressor-parity` · **pri:** `Medium` · **size:** `S` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:161`

## Acceptance

- [ ] Create a fresh native DiagnosticSuppressor fixture and compare real build with compile_check diagnostic/emit paths.
- [ ] If reproduced, honor applicable registered suppression semantics while preserving unavailable analyzer reporting, cancellation and unsuppressed compiler errors.
- [ ] Do not add pragmas, diagnostic allowlists, disabled checks or consumer-source workarounds. Honoring applicable registered DiagnosticSuppressor semantics is the investigated requirement, not a forbidden workaround. Close unsupported premise with evidence.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained historical evidence reports compile_check/native build divergence on branch-only tests. Current method reads raw compiler/emit diagnostics. Exact cause remains unproven; this is an independent investigation, not a claim the historical branch suite is deployed.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
