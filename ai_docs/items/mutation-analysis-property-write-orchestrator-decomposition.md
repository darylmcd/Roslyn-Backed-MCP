# mutation-analysis-property-write-orchestrator-decomposition — Decompose property-write analysis orchestration

**row:** `mutation-analysis-property-write-orchestrator-decomposition` · **pri:** `Low` · **size:** `S` · **deps:** `analysis-services-dedup-reference-classifiers`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:117-211`
- `tests/RoslynMcp.Tests/FindPropertyWritesPositionalRecordTests.cs`

## Acceptance

- [ ] Extract reference traversal and positional-record projection from `FindPropertyWritesWithMetadataAsync` without duplicating write classification.
- [ ] Keep each resulting method below cyclomatic complexity 10, nesting depth 4, and 80 logical lines.
- [ ] Preserve ordinary assignments, compound writes, `ref`/`out` writes, and positional-record constructor argument locations in focused regression tests.

## Evidence

- 2026-08-23 LocationDto Stage 1 adjacent review measured `FindPropertyWritesWithMetadataAsync` at cyclomatic complexity 11, 95 LOC, nesting depth 4, and maintainability index 37.65.
- The neighboring classifier consolidation is already tracked by `analysis-services-dedup-reference-classifiers`; sequence this orchestration extraction after it to avoid conflicting edits and duplicate classification helpers.
