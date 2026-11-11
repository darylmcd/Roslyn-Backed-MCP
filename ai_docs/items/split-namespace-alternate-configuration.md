# split-namespace-alternate-configuration — Preserve generated split namespaces across configurations

**row:** `split-namespace-alternate-configuration` · **pri:** `Medium` · **size:** `M` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/NamespaceMemberClassifier.cs`
- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `tests/RoslynMcp.Tests/ClassSplitReceivingNamespaceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/SplitServiceReceivingNamespaceIntegrationTests.cs`

## Acceptance

- [ ] Apply each split in one build configuration, then compile and exercise the generated types under both configurations when a conditional qualified namespace changes the enclosing identity. Preserve active-build behavior and directives.

## Evidence

- The pre-change emitters used only the innermost name in either configuration. The namespace correction emits the active namespace for the loaded configuration. Cold re-review traced the alternate configuration mismatch on the same FEATURE-dependent fixture.

## Notes

The loaded-configuration correction shipped in PR #2070 at 042a12b9de6833768011af000b430997321102e5. The focused fixture compiles and executes the loaded branch. Alternate-configuration qualification remains open.
