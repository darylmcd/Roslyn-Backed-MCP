# linked-copy-conditional-compilation-divergence — Handle #if differences between linked copies

**row:** `linked-copy-conditional-compilation-divergence` · **pri:** `Medium` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs` — `LinkedCopiesAsync` and the merge loop compare text, not the active preprocessor branch.
- `src/RoslynMcp.Roslyn/Services/InterfaceLinkedDocumentReconciler.cs` — `ReconcileLinkedCopiesAsync` writes one text into every copy.

## Acceptance

- [ ] Repro test first: a linked file whose `#if` branch differs per project (DefineConstants), with a reference to the interface name in only one branch.
- [ ] Either the re-pin edits each copy's active branch and every project compiles, or the preview refuses with a message that names the preprocessor difference; no zero-change success for an edit that applied to an inactive branch.

## Evidence

- iface-extraction-b report (PR 2148), round 2 follow-up 3: "`#if` differences between copies: misleading refusals and a silent no-op write (not touched; row, M)". Unverified by a test; the acceptance starts with the repro.
