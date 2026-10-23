# restore-package-root-authoritative-read-fail-closed — restore-package-root-authoritative-read-fail-closed

**row:** `restore-package-root-authoritative-read-fail-closed` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `tests/RoslynMcp.Tests/RestoreBoundaryRegressionTests.cs`

## Acceptance

- [ ] Malformed or unreadable existing assets cannot silently omit --packages or switch package storage; zero commands execute after refusal.
- [ ] Preserve an explicitly separate best-effort detector contract, valid absent/rootless controls, cancellation and safe diagnostics.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.
