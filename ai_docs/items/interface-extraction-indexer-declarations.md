# interface-extraction-indexer-declarations — Emit extracted indexers as indexer declarations

**row:** `interface-extraction-indexer-declarations` · **pri:** `High` · **size:** `M` · **deps:** `interface-extraction-ref-return-contracts,interface-member-projection-shared-emitter`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:220-232`, `:317-319` — indexer emitted as `PropertyDeclaration(type, "this[]")`; parameter namespaces not collected.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:508-511` and `CollectReferencedNamespaces` — same.
- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs` — legacy property-as-indexer case to remove.

## Acceptance

- [ ] Red first: `int this[int i] { get; }` extracts to an invalid `this[]` property today.
- [ ] Build `IndexerDeclaration(type).WithParameterList(BracketedParameterList(...))` from the shared parameter projection (see `cross-project-interface-parameter-semantics`), with public-only accessors.
- [ ] Remove the nullable projection's legacy property-as-indexer branch.
- [ ] An indexer whose parameter type lives in another namespace binds with no manual usings, in-project and cross-project, through preview, apply and reload.

## Evidence

- 2026-10-27: Roslyn reports `property.Name == "this[]"` for indexers; both emitters use it as the property name. No open row owned indexers (the public-accessor row defers them to a non-existent owner).

## Context

- Shares the parameter projection helper with `cross-project-interface-parameter-semantics`.
