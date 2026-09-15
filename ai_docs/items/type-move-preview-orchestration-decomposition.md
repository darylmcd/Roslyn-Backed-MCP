# type-move-preview-orchestration-decomposition

**row:** `type-move-preview-orchestration-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs` — PreviewMoveTypeToFileAsync
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`

## Acceptance

- [ ] Extract declaration selection and target-document planning into focused private helpers, leaving preview publication in the coordinator. Preserve the public preview shape and refusal messages.
- [ ] Keep scope to one production file and one test file, using existing preview/apply and refusal regressions to verify behavior.

## Evidence

Direct review found one method combining declaration-kind/ambiguity checks, source cardinality, target path validation, syntax rewriting, document creation, import cleanup, diff construction, and token publication. The context guards now have their own helper; the remaining coordinator needs the same ownership separation.
