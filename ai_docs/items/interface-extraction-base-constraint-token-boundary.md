# interface-extraction-base-constraint-token-boundary — Preserve base-list token boundaries

**row:** `interface-extraction-base-constraint-token-boundary` · **pri:** `High` · **size:** `M` · **deps:** `detached-test-fixture-source-root-metadata`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (standalone shared base-list writer consumer)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:177` (direct shared base-list writer consumer)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:703` (dependency-inversion/composite shared writer consumer)
- `src/RoslynMcp.Roslyn/Helpers/InterfaceBaseTypeWriter.cs`
- `tests/RoslynMcp.Tests/InterfaceBaseTypeInsertionIntegrationTests.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/InterfaceExtractionTestContext.cs`

## Acceptance

- [ ] Share trivia-preserving base-list insertion across both extractors, retaining their existing inline/multiline layout contracts.
- [ ] Preserve lexical boundaries before existing where clauses, comments, newlines and opening braces with no previous base and existing bases.
- [ ] Verify actual serialized preview text by reparsing, applying and reloading, including runtime interface calls and unrelated byte/comment preservation.
- [ ] Observe valid-before constrained-containing-type cases fail on current production before the repair; do not expand interface generic-declaration features.

## Evidence

- Current-session replacement-boundary-red.log: eight failed, two passed, zero skipped. Persisted preview text fuses IParameterService with where on supported unused constrained containing types; in-memory trees alone concealed the defect. Copied cross-project insertion also strips trailing base-type comments. The original broad candidate exposed this mechanism; the operator ordered its independent delivery after the fixture prerequisite, using primitive-member public-route regressions.

Operator split delivery: fixture prerequisite PR1841 first, then independent base-token writer, then complete semantic projection/handler contracts and receiving follow-ups. Dependency order never permits a newly broken intermediate; any exposed same-mechanism contract closure ships atomically. Preserve all original acceptance and red-then-green regressions. Full source candidate f6c2e9f9 remains retained.

- Prerequisite PR1841 merged at 34e749117aa5d6d6f829fc93dc17d80425325eaf with all six hosted shards, SDK-floor and CodeQL successful; local exact-head fixture suite22passed/0failed/6existingcapabilityskips. This slice contains only the shared base-list writer and all three producer call sites, plus direct regression/test-context reuse. No declaration projection, handler or receiving-context machinery is imported.
