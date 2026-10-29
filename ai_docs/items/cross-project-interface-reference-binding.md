# cross-project-interface-reference-binding — Bind generated interface uses to the generated symbol

**row:** `cross-project-interface-reference-binding` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (direct extraction base-list binding)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (dependency-inversion replacement type)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (shared extraction base-list binding)
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:92` (registration rewrite consumer)

## Acceptance

- [ ] Bind generated base lists, constructor parameter types and DI arguments to the actual namespace and interface symbol; preserve trivia and unrelated bindings.
- [ ] Cover distinct/nested/global/escaped namespaces, alias collisions and consumer project references through real public producers and compilation.
- [ ] Keep generic declaration parameters and signature dependency closure in their distinct rows; no broad import workaround.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current helpers emit bare resolvedInterfaceName/interfaceName while the interface is declared in the selected target namespace. Retained ExtractedInterfaceReference/OriginalNameBindings and consumer-edge changes are absent.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

Cold plan review 2026-10-29: first PR (slice 1a) covers extract, DI-inversion and same-project sites (CrossProjectRefactoringService :196/:310/:552, InterfaceExtractionService :90/:299). The ExtractAndWireOrchestrator registration rewrite and lost-update are NOT in it: tracked by extract-and-wire-semantic-registration-rewrite and extract-and-wire-same-file-composition (deps-chained). Close this row only when both are accounted for.
