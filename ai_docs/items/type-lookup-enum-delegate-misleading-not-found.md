# type-lookup-enum-delegate-misleading-not-found — Four type lookups still report "not found" for a selected enum or delegate

**row:** `type-lookup-enum-delegate-misleading-not-found` · **pri:** `Medium` · **size:** `M` · **verified:** `82be4c9b0e7f752e0d899554b037d175a2f6dfcd`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:44`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:191`
- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:39`
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:139`
- (new) `tests/RoslynMcp.Tests/CrossProjectUnsupportedKindRefusalTests.cs`

## Acceptance

- [ ] `extract_type_*` (TypeExtractionService.cs:44): selecting an enum or delegate returns the kind-naming refusal from `UnsupportedTypeKindRefusal` (tool name and supported kinds per caller), not "not found"; genuine miss keeps its text. Red-first test.
- [ ] `extract_interface_*` (InterfaceExtractionService.cs:191): same.
- [ ] `split_class_preview` (ClassSplitOrchestrator.cs:39): same.
- [ ] `ExtractAndWireOrchestrator.DetectDuplicateInterfaceAsync` (:139) quiet pre-check stays consistent with the refusal raised by BuildExtractInterfaceSolutionAsync (no divergent text).
- [ ] The differing not-found texts across these lookups are unified; `dependency_inversion_preview` kind handling is confirmed (it has no kind gate of its own).

## Evidence

- Cold review of the relocation enum/delegate refusal PR (rem/reloc, rows relocation-selected-enum-delegate-reported-not-found) listed these four `OfType<TypeDeclarationSyntax>` sites; the fixed routes are move_type_to_project, extract_interface_cross_project, extract_and_wire and dependency_inversion.

## Context

Same mechanism as relocation-selected-enum-delegate-reported-not-found: reuse `UnsupportedTypeKindRefusal` (added by that PR); one commit and one verdict per checkbox.
