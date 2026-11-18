# linked-copy-project-reference-duplicate-type — Link the generated interface instead of referencing the source project

**row:** `linked-copy-project-reference-duplicate-type` · **pri:** `Medium` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceLinkedDocumentReconciler.cs`:84-89 — adds `EnsureProjectReference` from every linked-copy project to the interface project.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` — `EnsureProjectReference`.
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` — standalone route calls the reconciler.

## Acceptance

- [ ] When a linked-copy project compiles its own copy of the source type, the generated interface file is linked into that project (or the preview refuses with a remedy naming that choice) instead of a ProjectReference that duplicates types.
- [ ] Red-first test: source type linked into a second project that uses the type name; before: gate refusal with CS0436/CS0433; after: pending and applied compile, interface implemented in both projects.

## Evidence

- iface-extraction-b report (PR 2148), round 2 follow-up 2 and final sketch: "New ProjectReference to a project that compiles the same public type gives CS0436/CS0433 ... Remaining fix = link the generated file instead of referencing (row, M)". The gate refuses, so nothing breaks silently.
- Verified at 12bc2c8b: reconciler loop at lines 84-89 references the interface project from every copy's project.
