# cross-project-interface-reference-binding — Bind generated interface uses to the generated symbol

**row:** `cross-project-interface-reference-binding` · **pri:** `High` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:177` (direct extraction base-list binding)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:288` (dependency-inversion replacement type)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:703` (shared extraction base-list binding)
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:92` (registration rewrite consumer)

## Acceptance

- [ ] Bind generated base lists, constructor parameter types and DI arguments to the actual namespace and interface symbol; preserve trivia and unrelated bindings.
- [ ] Cover distinct/nested/global/escaped namespaces, alias collisions and consumer project references through real public producers and compilation.
- [ ] Keep generic declaration parameters and signature dependency closure in their distinct rows; no broad import workaround.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current helpers emit bare resolvedInterfaceName/interfaceName while the interface is declared in the selected target namespace. Retained ExtractedInterfaceReference/OriginalNameBindings and consumer-edge changes are absent.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
