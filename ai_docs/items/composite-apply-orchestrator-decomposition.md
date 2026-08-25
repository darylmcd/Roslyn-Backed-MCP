# composite-apply-orchestrator-decomposition — Bound composite apply orchestration

**row:** `composite-apply-orchestrator-decomposition` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Extract file-mutation execution and partial-failure projection into focused helpers; reduce `ApplyCompositeAsync` to at most complexity 6 and 60 logical lines.
- [ ] Preserve delete/write behavior, source encoding, cancellation, reload, token invalidation, change tracking, and secret-safe failure reporting.
- [ ] Replace historical row-id narrative comments with short invariant-focused documentation.
- [ ] One table-driven regression covers delete, encoded write, pre-write failure, partial apply, reload failure, and successful invalidation.

## Evidence

The 2026-08-24 adjacent quality review measured `ApplyCompositeAsync` at cyclomatic complexity 11, 109 logical lines, nesting depth 4, and maintainability index 35.93. The method combines validation, filesystem mutation, encoding recovery, workspace lifecycle, change tracking, redacted failure projection, and user-message construction, making its partial-apply contract difficult to review safely.
