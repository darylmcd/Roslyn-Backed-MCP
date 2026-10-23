# refactoring-test-minimal-native-fixtures — refactoring-test-minimal-native-fixtures

**row:** `refactoring-test-minimal-native-fixtures` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs:41`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs:67`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:1`
- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs:9`

## Acceptance

- [ ] Classify each case by required project/reference/package topology before changing fixtures.
- [ ] Use small fresh native projects for self-contained cases; preserve dedicated multi-project, constructor/runtime, metadata, package and authority scenarios.
- [ ] Retain real preview/apply, complete text/byte expectations, version and undo checks; no blanket conversion to in-memory stubs.
- [ ] Measure setup and request costs separately; prefer fresh small fixtures over a resettable shared mutable sample graph.

## Evidence

Seventy-five files have 578 copied-sample helper callsites. ChangeSignaturePreviewTests creates a full sample copy, adds a self-contained source file, loads all projects, then applies the preview even for single-method formatting cases. No savings estimate has been measured.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.
