# compile-check-diagnostic-suppressor-parity — Investigate suppressor parity with native compiler results

**row:** `compile-check-diagnostic-suppressor-parity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:161`

## Acceptance

- [ ] Create a fresh native DiagnosticSuppressor fixture and compare real build with compile_check diagnostic/emit paths.
- [ ] If reproduced, honor applicable registered suppression semantics while preserving unavailable analyzer reporting, cancellation and unsuppressed compiler errors.
- [ ] Do not add pragmas, diagnostic allowlists, disabled checks or consumer-source workarounds. Honoring applicable registered DiagnosticSuppressor semantics is the investigated requirement, not a forbidden workaround. Close unsupported premise with evidence.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained historical evidence reports compile_check/native build divergence on branch-only tests. Current method reads raw compiler/emit diagnostics. Exact cause remains unproven; this is an independent investigation, not a claim the historical branch suite is deployed.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
