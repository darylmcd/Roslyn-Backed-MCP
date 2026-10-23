# native-restore-co-storage-closure-authority — native-restore-co-storage-closure-authority

**row:** `native-restore-co-storage-closure-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`
- `tests/RoslynMcp.Tests/TestRunnerEvaluationPropertiesTests.cs`

## Acceptance

- [ ] Planning, current path authorization, and pre/post execution receipts use one identical expanded closure.
- [ ] A denied or changed sibling-only dependency cannot be read or restored; retain exact multi-target shared-storage and isolated-context positive controls.

## Evidence

- Independent current-source restore review on 2026-10-16 identified this mechanism. Parent serializes causal regression tests before correction.

## Context

- Required complete-fix companion of the operator-authorized restore initiative; do not defer defects exposed by the active change.
