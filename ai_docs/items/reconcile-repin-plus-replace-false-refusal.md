# reconcile-repin-plus-replace-false-refusal — Accept compatible linked-copy edits after a re-pin

**row:** `reconcile-repin-plus-replace-false-refusal` · **pri:** `Medium` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceLinkedDocumentReconciler.cs`:74-82 — refuses when changed copies differ, without checking whether one change contains the other.
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` — replaceUsages retype after the re-pin (standalone route).
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` — dependency-inversion constructor rewrite after the re-pin.

## Acceptance

- [ ] A linked consumer file edited identically in every copy by the re-pin and then in one copy by replaceUsages (standalone) or the constructor rewrite (dependency inversion) yields one final text in every copy; the pending and applied solutions compile.
- [ ] Copies whose edits genuinely conflict (neither text contains the other edit set) are still refused with the route remedy.
- [ ] Red-first test per route; mutation check: restore the plain divergence comparison and see the new tests fail.

## Evidence

- iface-extraction-b report (PR 2148), rounds 3 and 4: sketches `reconcile-repin-plus-replace-false-refusal` and F1 — "a group edited by the re-pin in every copy and by replaceUsages in one copy diverges and is refused although the edits are compatible". Not fixed in PR 2148.
- Verified at 12bc2c8b: `ReconcileLinkedCopiesAsync` compares `changedTexts` with `Distinct` and throws on any difference (InterfaceLinkedDocumentReconciler.cs:76-82).
