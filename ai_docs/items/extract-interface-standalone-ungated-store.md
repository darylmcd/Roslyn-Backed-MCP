# extract-interface-standalone-ungated-store — Compile-gate every standalone extract_interface preview

**row:** `extract-interface-standalone-ungated-store` · **pri:** `Medium` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`:146-152 — gate runs only when `replaceUsages || sameNamedType || linkedCopyChanged`.
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`:168 — `_previewStore.Store`.

## Acceptance

- [ ] Every standalone preview that adds the interface runs the gate with a remedy that fits the cause; a projection that does not compile in the source project is refused and stores no token.
- [ ] Red-first test of an uncompilable projection on the plain route (e.g. a member whose signature needs a type the interface file cannot see); changelog Migration note for the new refusal.

## Evidence

- iface-extraction report (PR 2145), sketch 2 and iface-projector report (PR 2147), sketch 2: standalone route stores a token with no gate when replaceUsages=false, while cross-project (CrossProjectRefactoringService.cs:137,:340) and extract-and-wire (ExtractAndWireOrchestrator.cs:99) gate.
- Verified at 12bc2c8b: InterfaceExtractionService.cs:147 condition.

## Context

- Per-producer row demanded by `preview-stores-without-compile-delta-audit` acceptance bullet 2 (that row anchors InterfaceExtractionService.cs:133, now :168).
