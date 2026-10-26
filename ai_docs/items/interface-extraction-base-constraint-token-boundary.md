# interface-extraction-base-constraint-token-boundary — Preserve base-list token boundaries

**row:** `interface-extraction-base-constraint-token-boundary` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (base-list insertion)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (AddBaseType and AppendBaseTypeWithTrailingComma)
- `tests/RoslynMcp.Tests/InterfaceDeclarationFidelityIntegrationTests.cs`

## Acceptance

- [ ] Share trivia-preserving base-list insertion across both extractors, retaining their existing inline/multiline layout contracts.
- [ ] Preserve lexical boundaries before existing where clauses, comments, newlines and opening braces with no previous base and existing bases.
- [ ] Verify actual serialized preview text by reparsing, applying and reloading, including runtime interface calls and unrelated byte/comment preservation.
- [ ] Observe valid-before constrained-containing-type cases fail on current production before the repair; do not expand interface generic-declaration features.

## Evidence

- Current-session replacement-boundary-red.log: eight failed, two passed, zero skipped. Persisted preview text fuses IParameterService with where on supported unused constrained containing types; in-memory trees alone concealed the defect. Copied cross-project insertion also strips trailing base-type comments. This same declaration-boundary mechanism was exposed by the parameter fidelity fix and must be repaired in the same PR.
