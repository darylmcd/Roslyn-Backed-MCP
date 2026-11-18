# extract-and-wire-di-registration-single-copy-reference — Treat single-copy DI registration files like linked ones

**row:** `extract-and-wire-di-registration-single-copy-reference` · **pri:** `Low` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs`:71-99 — DI registration rewrite, reconcile, gate.
- `src/RoslynMcp.Roslyn/Services/InterfaceLinkedDocumentReconciler.cs`:63 — returns early for fewer than two copies, so no `EnsureProjectReference`.

## Acceptance

- [ ] A DI registration file in a project that does not reference the interface project is handled the same whether it is linked or not: either the reference is added in both cases, or both are refused with one remedy naming the project.
- [ ] Red-first test with a single-copy registration file in a project lacking the reference; pending and applied compile, or the refusal text is asserted.

## Evidence

- iface-extraction-b report (PR 2148), round 4 sketch F2: "extract-and-wire adds a project reference only for linked DI registration files, so a single-copy registration file in a project without a reference to the interface project is refused by the gate: inconsistent."
- Verified at 12bc2c8b: reconciler line 63 `if (linked.Length < 2 ...) return pending;` precedes the `EnsureProjectReference` loop (:84-89).
